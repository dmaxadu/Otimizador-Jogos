using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OtimizadorJogos.Core;

/// <summary>Resultado de uma operação de aplicar/reverter.</summary>
public sealed class ResultadoOperacao
{
    public int Aplicados { get; set; }

    public int Revertidos { get; set; }

    public List<string> Erros { get; } = [];

    /// <summary>null = não tentou (já havia backup); true/false = resultado da tentativa.</summary>
    public bool? PontoRestauracaoCriado { get; set; }

    public bool HouveMudanca => Aplicados + Revertidos > 0;

    public string Resumo()
    {
        var partes = new List<string>();
        if (Aplicados > 0)
        {
            partes.Add(Aplicados == 1 ? "1 ajuste aplicado" : $"{Aplicados} ajustes aplicados");
        }

        if (Revertidos > 0)
        {
            partes.Add(Revertidos == 1 ? "1 ajuste revertido" : $"{Revertidos} ajustes revertidos");
        }

        if (Erros.Count > 0)
        {
            partes.Add(Erros.Count == 1 ? "1 erro" : $"{Erros.Count} erros");
        }

        return partes.Count == 0 ? "Nenhuma alteração feita." : string.Join(", ", partes) + ".";
    }
}

/// <summary>
/// Aplica e reverte os ajustes. Regra de ouro: o estado original é gravado no backup
/// ANTES de qualquer alteração, então tudo o que foi mexido sempre pode ser desfeito.
/// </summary>
public sealed partial class Otimizador
{
    public const string VersaoBackup = "2.0";
    public const string GuidAltoDesempenho = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private const string ChaveMouse = @"HKCU:\Control Panel\Mouse";

    private readonly IRegistro _registro;
    private readonly ISistema _sistema;
    private readonly ArmazenamentoBackup _armazenamento;
    private readonly IReadOnlyList<Ajuste> _catalogo;

    public Otimizador(IRegistro registro, ISistema sistema, ArmazenamentoBackup armazenamento, IReadOnlyList<Ajuste>? catalogo = null)
    {
        _registro = registro;
        _sistema = sistema;
        _armazenamento = armazenamento;
        _catalogo = catalogo ?? CatalogoAjustes.Todos;
    }

    public IReadOnlyList<Ajuste> Catalogo => _catalogo;

    [GeneratedRegex(@"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}")]
    private static partial Regex RegexGuid();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+$")]
    private static partial Regex RegexNomeServico();

    [GeneratedRegex(@":\s*4\s+[A-Z_]+")]
    private static partial Regex RegexServicoRodando();

    // -----------------------------------------------------------------------
    // Consulta
    // -----------------------------------------------------------------------

    public IReadOnlyList<EstadoAjuste> ObterEstados()
    {
        var backup = _armazenamento.Carregar();
        var aplicados = new HashSet<string>(
            backup?.Itens.Select(i => i.Id) ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var planoCriado = backup?.Itens.FirstOrDefault(i => EhTipo(i, TipoAjuste.PlanoEnergia))?.PlanoCriado;

        return _catalogo
            .Select(a => new EstadoAjuste(a, aplicados.Contains(a.Id), EstaNoAlvo(a, planoCriado)))
            .ToList();
    }

    private bool EstaNoAlvo(Ajuste ajuste, string? planoCriado)
    {
        try
        {
            switch (ajuste.Tipo)
            {
                case TipoAjuste.Registro:
                    return ajuste.Valores.All(v =>
                        _registro.TentarLer(v.Caminho, v.Nome, out _, out var atual)
                        && string.Equals(ConversorValor.Texto(atual), ConversorValor.Texto(v.Valor), StringComparison.OrdinalIgnoreCase));

                case TipoAjuste.PlanoEnergia:
                    var ativo = ObterPlanoAtivo();
                    return ativo == GuidAltoDesempenho
                           || (planoCriado is not null && ativo == planoCriado.ToLowerInvariant());

                case TipoAjuste.Servico:
                    var estado = LerEstadoServico(ajuste.Servico!);
                    return estado is not null && estado.Start == 4;

                default:
                    return false;
            }
        }
        catch (Exception)
        {
            // Consulta é só informativa; um erro aqui não deve impedir a tela de abrir
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Aplicar / reverter
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reverte os ajustes em <paramref name="reverter"/> e aplica os de <paramref name="ativar"/>.
    /// Ajustes já aplicados são ignorados (o backup original nunca é sobrescrito).
    /// </summary>
    public ResultadoOperacao Executar(
        IReadOnlyCollection<Ajuste> ativar,
        IReadOnlyCollection<string> reverter,
        IProgress<string>? progresso = null)
    {
        var resultado = new ResultadoOperacao();
        var backup = _armazenamento.Carregar();

        // 1) Reverter, na ordem inversa da aplicação
        if (backup is not null && reverter.Count > 0)
        {
            var alvo = new HashSet<string>(reverter, StringComparer.OrdinalIgnoreCase);
            var itens = backup.Itens.Where(i => alvo.Contains(i.Id)).Reverse().ToList();
            foreach (var item in itens)
            {
                var nome = NomeDe(item.Id);
                progresso?.Report($"Revertendo: {nome}");
                try
                {
                    ReverterItem(item);
                    backup.Itens.Remove(item);
                    _armazenamento.Salvar(backup);
                    resultado.Revertidos++;
                    _armazenamento.Log($"Revertido: {nome}");
                }
                catch (Exception ex)
                {
                    resultado.Erros.Add($"{nome}: {ex.Message}");
                    _armazenamento.Log($"ERRO ao reverter {nome}: {ex.Message}");
                }
            }
        }

        // 2) Aplicar os que ainda não estão no backup
        var novos = ativar
            .Where(a => backup is null || !backup.Itens.Any(i => string.Equals(i.Id, a.Id, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (novos.Count > 0)
        {
            if (backup is null)
            {
                progresso?.Report("Criando ponto de restauração do Windows...");
                resultado.PontoRestauracaoCriado = TentarCriarPontoRestauracao();
                _armazenamento.Log(resultado.PontoRestauracaoCriado == true
                    ? "Ponto de restauração criado."
                    : "Ponto de restauração não criado (limite de 24 h ou recurso desligado).");

                backup = new Backup
                {
                    Versao = VersaoBackup,
                    Data = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Usuario = Environment.UserName,
                };
                _armazenamento.Salvar(backup);
            }

            foreach (var ajuste in novos)
            {
                progresso?.Report($"Aplicando: {ajuste.Titulo}");
                try
                {
                    AplicarItem(ajuste, backup);
                    resultado.Aplicados++;
                    _armazenamento.Log($"Aplicado: {ajuste.Titulo}");
                }
                catch (Exception ex)
                {
                    resultado.Erros.Add($"{ajuste.Titulo}: {ex.Message}");
                    _armazenamento.Log($"ERRO ao aplicar {ajuste.Titulo}: {ex.Message}");
                }
            }
        }

        // 3) Sem nada aplicado: arquiva o backup (mesmo comportamento do script)
        if (backup is not null && backup.Itens.Count == 0 && _armazenamento.Existe)
        {
            var arquivo = _armazenamento.Arquivar();
            _armazenamento.Log($"Tudo revertido. Backup arquivado em {arquivo}");
        }

        return resultado;
    }

    /// <summary>Reverte todos os ajustes que estão no backup.</summary>
    public ResultadoOperacao ReverterTudo(IProgress<string>? progresso = null)
    {
        var backup = _armazenamento.Carregar();
        var ids = backup?.Itens.Select(i => i.Id).ToList() ?? [];
        return Executar([], ids, progresso);
    }

    private void AplicarItem(Ajuste ajuste, Backup backup)
    {
        switch (ajuste.Tipo)
        {
            case TipoAjuste.Registro:
            {
                // Guarda o estado de TODOS os valores antes de mexer em qualquer um
                var estados = ajuste.Valores.Select(v => LerEstadoRegistro(v.Caminho, v.Nome)).ToList();
                backup.Itens.Add(new ItemBackup { Id = ajuste.Id, Tipo = nameof(TipoAjuste.Registro), Estados = estados });
                _armazenamento.Salvar(backup);

                foreach (var v in ajuste.Valores)
                {
                    _registro.Gravar(v.Caminho, v.Nome, v.Tipo, v.Valor);
                }

                ExecutarPosterior(ajuste.Posterior);
                break;
            }

            case TipoAjuste.PlanoEnergia:
            {
                var item = new ItemBackup
                {
                    Id = ajuste.Id,
                    Tipo = nameof(TipoAjuste.PlanoEnergia),
                    PlanoAnterior = ObterPlanoAtivo(),
                };
                backup.Itens.Add(item);
                _armazenamento.Salvar(backup);

                var alvo = GuidAltoDesempenho;
                if (!PlanoExiste(alvo))
                {
                    // Plano escondido neste Windows: cria uma cópia e anota para apagar ao reverter
                    var (codigo, saida) = _sistema.Executar("powercfg.exe", $"-duplicatescheme {GuidAltoDesempenho}");
                    var guid = RegexGuid().Match(saida);
                    if (codigo != 0 || !guid.Success)
                    {
                        throw new InvalidOperationException("Não foi possível criar o plano Alto desempenho neste PC.");
                    }

                    alvo = guid.Value.ToLowerInvariant();
                    item.PlanoCriado = alvo;
                    _armazenamento.Salvar(backup);
                }

                ExigirSucesso(_sistema.Executar("powercfg.exe", $"/setactive {alvo}"), "powercfg");
                break;
            }

            case TipoAjuste.Servico:
            {
                var nome = ajuste.Servico ?? throw new InvalidOperationException("Ajuste de serviço sem nome de serviço.");
                var estado = LerEstadoServico(nome)
                             ?? throw new InvalidOperationException($"O serviço {nome} não existe neste Windows.");
                backup.Itens.Add(new ItemBackup { Id = ajuste.Id, Tipo = nameof(TipoAjuste.Servico), Estado = estado });
                _armazenamento.Salvar(backup);

                if (estado.Rodando)
                {
                    _sistema.Executar("sc.exe", $"stop {nome}");
                }

                ExigirSucesso(_sistema.Executar("sc.exe", $"config {nome} start= disabled"), "sc");
                break;
            }

            default:
                throw new InvalidOperationException($"Tipo de ajuste desconhecido: {ajuste.Tipo}");
        }
    }

    private void ReverterItem(ItemBackup item)
    {
        if (EhTipo(item, TipoAjuste.Registro))
        {
            var estados = item.Estados ?? [];
            foreach (var estado in Enumerable.Reverse(estados))
            {
                RestaurarRegistro(estado);
            }

            var definicao = _catalogo.FirstOrDefault(a => string.Equals(a.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            if (definicao is not null)
            {
                ExecutarPosterior(definicao.Posterior);
            }
        }
        else if (EhTipo(item, TipoAjuste.PlanoEnergia))
        {
            var anterior = item.PlanoAnterior ?? "";
            if (!RegexGuid().IsMatch(anterior))
            {
                throw new InvalidDataException("O plano de energia anterior no backup é inválido.");
            }

            ExigirSucesso(_sistema.Executar("powercfg.exe", $"/setactive {anterior}"), "powercfg");

            if (!string.IsNullOrEmpty(item.PlanoCriado) && RegexGuid().IsMatch(item.PlanoCriado) && PlanoExiste(item.PlanoCriado))
            {
                _sistema.Executar("powercfg.exe", $"/delete {item.PlanoCriado}");
            }
        }
        else if (EhTipo(item, TipoAjuste.Servico))
        {
            var estado = item.Estado ?? throw new InvalidDataException("Estado do serviço faltando no backup.");
            if (!RegexNomeServico().IsMatch(estado.Nome))
            {
                throw new InvalidDataException("Nome de serviço inválido no backup.");
            }

            ExigirSucesso(_sistema.Executar("sc.exe", $"config {estado.Nome} start= {ModoInicio(estado)}"), "sc");
            if (estado.Rodando)
            {
                _sistema.Executar("sc.exe", $"start {estado.Nome}");
            }
        }
        else
        {
            throw new InvalidDataException($"Tipo de item desconhecido no backup: {item.Tipo}");
        }
    }

    // -----------------------------------------------------------------------
    // Registro
    // -----------------------------------------------------------------------

    private EstadoRegistro LerEstadoRegistro(string caminho, string nome)
    {
        var estado = new EstadoRegistro
        {
            Caminho = caminho,
            Nome = nome,
            ChaveExistia = _registro.ChaveExiste(caminho),
        };

        if (estado.ChaveExistia && _registro.TentarLer(caminho, nome, out var tipo, out var valor) && valor is not null)
        {
            estado.ValorExistia = true;
            estado.Tipo = tipo.ToString();
            estado.Valor = ConversorValor.ParaJson(valor, tipo);
        }

        return estado;
    }

    private void RestaurarRegistro(EstadoRegistro estado)
    {
        if (estado.ValorExistia)
        {
            var tipo = Enum.Parse<RegistryValueKind>(estado.Tipo ?? "String", ignoreCase: true);
            _registro.Gravar(estado.Caminho, estado.Nome, tipo, ConversorValor.DeJson(estado.Valor, tipo));
            return;
        }

        // O valor não existia antes: apaga
        if (!_registro.ChaveExiste(estado.Caminho))
        {
            return;
        }

        if (_registro.TentarLer(estado.Caminho, estado.Nome, out _, out _))
        {
            _registro.ApagarValor(estado.Caminho, estado.Nome);
        }

        // A chave também não existia e ficou vazia: apaga
        if (!estado.ChaveExistia && _registro.ChaveVazia(estado.Caminho))
        {
            _registro.ApagarChave(estado.Caminho);
        }
    }

    private void ExecutarPosterior(AcaoPosterior acao)
    {
        try
        {
            switch (acao)
            {
                case AcaoPosterior.AtualizarMouse:
                    _sistema.AtualizarMouse(
                        LerInteiroMouse("MouseThreshold1", 6),
                        LerInteiroMouse("MouseThreshold2", 10),
                        LerInteiroMouse("MouseSpeed", 1));
                    break;

                case AcaoPosterior.AvisarMudancaTema:
                    _sistema.AvisarMudancaTema();
                    break;
            }
        }
        catch (Exception ex)
        {
            // Os valores já estão gravados; no pior caso o efeito vale após sair e entrar na conta
            _armazenamento.Log($"Aviso: não deu para aplicar na hora ({acao}): {ex.Message}");
        }
    }

    private int LerInteiroMouse(string nome, int padrao) =>
        _registro.TentarLer(ChaveMouse, nome, out _, out var valor)
        && int.TryParse(ConversorValor.Texto(valor), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero)
            ? numero
            : padrao;

    // -----------------------------------------------------------------------
    // Plano de energia
    // -----------------------------------------------------------------------

    private string ObterPlanoAtivo()
    {
        var (_, saida) = _sistema.Executar("powercfg.exe", "/getactivescheme");
        var guid = RegexGuid().Match(saida);
        return guid.Success
            ? guid.Value.ToLowerInvariant()
            : throw new InvalidOperationException("Não foi possível ler o plano de energia ativo.");
    }

    private bool PlanoExiste(string guid)
    {
        var (_, saida) = _sistema.Executar("powercfg.exe", "/list");
        return saida.Contains(guid, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Serviços
    // -----------------------------------------------------------------------

    private EstadoServico? LerEstadoServico(string nome)
    {
        var caminho = $@"HKLM:\SYSTEM\CurrentControlSet\Services\{nome}";
        if (!_registro.ChaveExiste(caminho))
        {
            return null;
        }

        var start = _registro.TentarLer(caminho, "Start", out _, out var valorStart)
            ? Convert.ToInt32(valorStart, CultureInfo.InvariantCulture)
            : 3;
        var atrasado = _registro.TentarLer(caminho, "DelayedAutostart", out _, out var valorAtrasado)
                       && Convert.ToInt32(valorAtrasado, CultureInfo.InvariantCulture) == 1;

        var (_, saida) = _sistema.Executar("sc.exe", $"query {nome}");
        var rodando = saida.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) || RegexServicoRodando().IsMatch(saida);

        return new EstadoServico { Nome = nome, Start = start, Atrasado = atrasado, Rodando = rodando };
    }

    private static string ModoInicio(EstadoServico estado) => estado.Start switch
    {
        2 => estado.Atrasado ? "delayed-auto" : "auto",
        3 => "demand",
        4 => "disabled",
        _ => "demand",
    };

    // -----------------------------------------------------------------------
    // Utilitários
    // -----------------------------------------------------------------------

    private bool TentarCriarPontoRestauracao()
    {
        try
        {
            // -WarningAction Stop: se o Windows recusar (limite de 1 por 24 h), sai com código de erro
            var (codigo, _) = _sistema.Executar(
                "powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Checkpoint-Computer "
                + "-Description 'Antes do Otimizador de Jogos' -RestorePointType MODIFY_SETTINGS "
                + "-WarningAction Stop -ErrorAction Stop\"",
                TimeSpan.FromMinutes(3));
            return codigo == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ExigirSucesso((int Codigo, string Saida) resultado, string programa)
    {
        if (resultado.Codigo != 0)
        {
            var detalhe = resultado.Saida.Trim();
            throw new InvalidOperationException(
                $"{programa} retornou o código {resultado.Codigo}{(detalhe.Length > 0 ? $": {detalhe}" : ".")}");
        }
    }

    private static bool EhTipo(ItemBackup item, TipoAjuste tipo) =>
        string.Equals(item.Tipo, tipo.ToString(), StringComparison.OrdinalIgnoreCase);

    private string NomeDe(string id) =>
        _catalogo.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))?.Titulo ?? id;
}
