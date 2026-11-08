namespace OtimizadorJogos.Core.ViewModels;

/// <summary>Um cartão da lista. O interruptor (<see cref="Selecionado"/>) só vira mudança real ao clicar em "Aplicar".</summary>
public sealed class AjusteViewModel : ObservableObject
{
    private readonly Action _aoMudarSelecao;
    private bool _aplicado;
    private bool _jaNoSistema;
    private bool _selecionado;

    public AjusteViewModel(Ajuste definicao, Action aoMudarSelecao)
    {
        Definicao = definicao;
        _aoMudarSelecao = aoMudarSelecao;
    }

    public Ajuste Definicao { get; }

    public string Titulo => Definicao.Titulo;

    public string Descricao => Definicao.Descricao;

    public string Icone => Definicao.Icone;

    public string Categoria => Definicao.Categoria;

    public bool RequerReinicio => Definicao.RequerReinicio;

    public string TextoImpacto => Definicao.Impacto switch
    {
        Impacto.Alto => "Impacto alto",
        Impacto.Medio => "Impacto médio",
        Impacto.Varia => "Impacto variável",
        _ => "Impacto baixo",
    };

    /// <summary>Está aplicado de fato (existe no backup).</summary>
    public bool Aplicado => _aplicado;

    /// <summary>Posição do interruptor na tela.</summary>
    public bool Selecionado
    {
        get => _selecionado;
        set
        {
            if (Set(ref _selecionado, value))
            {
                NotificarEstado();
                _aoMudarSelecao();
            }
        }
    }

    public bool Pendente => _selecionado != _aplicado;

    /// <summary>"Ativo", "Pendente" ou "Original" — usado para as cores na tela.</summary>
    public string StatusTipo => Pendente ? "Pendente" : _aplicado ? "Ativo" : "Original";

    public string StatusTexto
    {
        get
        {
            if (Pendente)
            {
                return _selecionado ? "Será ativado" : "Será revertido";
            }

            if (_aplicado)
            {
                return "Ativo";
            }

            return _jaNoSistema ? "Já estava assim no seu PC" : "Original do Windows";
        }
    }

    /// <summary>Atualiza com o estado real lido do sistema e descarta mudanças pendentes.</summary>
    public void AtualizarEstado(bool aplicado, bool jaNoSistema)
    {
        _aplicado = aplicado;
        _jaNoSistema = jaNoSistema;
        _selecionado = aplicado;
        OnPropertyChanged(nameof(Aplicado));
        OnPropertyChanged(nameof(Selecionado));
        NotificarEstado();
    }

    private void NotificarEstado()
    {
        OnPropertyChanged(nameof(Pendente));
        OnPropertyChanged(nameof(StatusTipo));
        OnPropertyChanged(nameof(StatusTexto));
    }
}
