using System.Collections.ObjectModel;

namespace OtimizadorJogos.Core.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly Otimizador _otimizador;
    private readonly ArmazenamentoBackup _armazenamento;
    private readonly ISistema _sistema;

    private bool _ocupado;
    private string _mensagemOcupado = "";
    private string _status = "Lendo as configurações do seu PC...";
    private bool _mostrarAvisoReinicio;

    public MainViewModel(Otimizador otimizador, ArmazenamentoBackup armazenamento, ISistema sistema)
    {
        _otimizador = otimizador;
        _armazenamento = armazenamento;
        _sistema = sistema;

        Ajustes = new ObservableCollection<AjusteViewModel>(
            otimizador.Catalogo.Select(a => new AjusteViewModel(a, AoMudarSelecao)));

        AplicarCommand = new RelayCommand(() => _ = AplicarAsync(), () => !Ocupado && Pendentes > 0);
        DescartarCommand = new RelayCommand(Descartar, () => !Ocupado && Pendentes > 0);
        RestaurarTudoCommand = new RelayCommand(() => _ = RestaurarTudoAsync(), () => !Ocupado && Ativos > 0);
        MarcarTodosCommand = new RelayCommand(MarcarTodos, () => !Ocupado && Ajustes.Any(a => !a.Selecionado));
        AbrirPastaCommand = new RelayCommand(AbrirPasta);
    }

    public ObservableCollection<AjusteViewModel> Ajustes { get; }

    public RelayCommand AplicarCommand { get; }

    public RelayCommand DescartarCommand { get; }

    public RelayCommand RestaurarTudoCommand { get; }

    public RelayCommand MarcarTodosCommand { get; }

    public RelayCommand AbrirPastaCommand { get; }

    /// <summary>Pergunta sim/não ao usuário (título, mensagem). Definido pela janela.</summary>
    public Func<string, string, bool>? Confirmar { get; set; }

    /// <summary>Mostra um aviso (título, mensagem). Definido pela janela.</summary>
    public Action<string, string>? MostrarMensagem { get; set; }

    public bool Ocupado
    {
        get => _ocupado;
        private set
        {
            if (Set(ref _ocupado, value))
            {
                AtualizarComandos();
            }
        }
    }

    public string MensagemOcupado
    {
        get => _mensagemOcupado;
        private set => Set(ref _mensagemOcupado, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool MostrarAvisoReinicio
    {
        get => _mostrarAvisoReinicio;
        private set => Set(ref _mostrarAvisoReinicio, value);
    }

    public int Pendentes => Ajustes.Count(a => a.Pendente);

    public int Ativos => Ajustes.Count(a => a.Aplicado);

    public string TextoResumo => $"{Ativos} de {Ajustes.Count} ajustes ativos";

    public string TextoPendentes => Pendentes switch
    {
        0 => "Nenhuma alteração pendente",
        1 => "1 alteração pendente",
        var n => $"{n} alterações pendentes",
    };

    public async Task CarregarAsync()
    {
        Ocupado = true;
        MensagemOcupado = "Lendo as configurações do seu PC...";
        try
        {
            await RecarregarEstadosAsync();
            Status = Ativos > 0
                ? "Os ajustes ativos podem ser revertidos a qualquer momento."
                : "Escolha os ajustes e clique em Aplicar alterações.";
        }
        catch (Exception ex)
        {
            Status = "Não foi possível ler as configurações.";
            MostrarMensagem?.Invoke("Erro ao ler as configurações", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    private async Task AplicarAsync()
    {
        var ativar = Ajustes.Where(a => a.Pendente && a.Selecionado).Select(a => a.Definicao).ToList();
        var reverter = Ajustes.Where(a => a.Pendente && !a.Selecionado).Select(a => a.Definicao.Id).ToList();
        if (ativar.Count == 0 && reverter.Count == 0)
        {
            return;
        }

        await ExecutarOperacaoAsync(
            "Preparando...",
            progresso => _otimizador.Executar(ativar, reverter, progresso));
    }

    private async Task RestaurarTudoAsync()
    {
        var confirmado = Confirmar?.Invoke(
            "Restaurar tudo",
            "Todos os ajustes serão desfeitos e o Windows volta exatamente como estava antes.\n\nDeseja continuar?") ?? true;
        if (!confirmado)
        {
            return;
        }

        await ExecutarOperacaoAsync("Restaurando...", progresso => _otimizador.ReverterTudo(progresso));
    }

    private async Task ExecutarOperacaoAsync(string mensagemInicial, Func<IProgress<string>, ResultadoOperacao> operacao)
    {
        Ocupado = true;
        MensagemOcupado = mensagemInicial;

        // Criado na thread da interface: os relatórios de progresso voltam para ela
        var progresso = new Progress<string>(m => MensagemOcupado = m);
        ResultadoOperacao? resultado = null;
        try
        {
            resultado = await Task.Run(() => operacao(progresso));
            MensagemOcupado = "Conferindo o resultado...";
            await RecarregarEstadosAsync();
        }
        catch (Exception ex)
        {
            MostrarMensagem?.Invoke("Erro", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }

        if (resultado is null)
        {
            return;
        }

        Status = resultado.Resumo();
        if (resultado.HouveMudanca)
        {
            MostrarAvisoReinicio = true;
        }

        if (resultado.Erros.Count > 0)
        {
            MostrarMensagem?.Invoke(
                "Alguns ajustes não foram concluídos",
                string.Join("\n", resultado.Erros)
                + "\n\nO que chegou a ser alterado está no backup e pode ser revertido.");
        }
        else if (resultado.PontoRestauracaoCriado == false)
        {
            Status += " O Windows não criou um ponto de restauração agora, mas o backup do app cobre a reversão.";
        }
    }

    private async Task RecarregarEstadosAsync()
    {
        var estados = await Task.Run(_otimizador.ObterEstados);
        foreach (var estado in estados)
        {
            var vm = Ajustes.FirstOrDefault(a => a.Definicao.Id == estado.Ajuste.Id);
            vm?.AtualizarEstado(estado.Aplicado, estado.JaNoSistema);
        }

        AtualizarResumo();
    }

    private void Descartar()
    {
        foreach (var ajuste in Ajustes.Where(a => a.Pendente))
        {
            ajuste.Selecionado = ajuste.Aplicado;
        }
    }

    private void MarcarTodos()
    {
        foreach (var ajuste in Ajustes)
        {
            ajuste.Selecionado = true;
        }
    }

    private void AbrirPasta()
    {
        try
        {
            _sistema.AbrirPasta(_armazenamento.Pasta);
        }
        catch (Exception ex)
        {
            MostrarMensagem?.Invoke("Não foi possível abrir a pasta", ex.Message);
        }
    }

    private void AoMudarSelecao() => AtualizarResumo();

    private void AtualizarResumo()
    {
        OnPropertyChanged(nameof(Pendentes));
        OnPropertyChanged(nameof(Ativos));
        OnPropertyChanged(nameof(TextoResumo));
        OnPropertyChanged(nameof(TextoPendentes));
        AtualizarComandos();
    }

    private void AtualizarComandos()
    {
        AplicarCommand.NotificarMudanca();
        DescartarCommand.NotificarMudanca();
        RestaurarTudoCommand.NotificarMudanca();
        MarcarTodosCommand.NotificarMudanca();
    }
}
