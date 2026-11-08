using Microsoft.Win32;

namespace OtimizadorJogos.Core;

/// <summary>Como o ajuste é aplicado no sistema.</summary>
public enum TipoAjuste
{
    Registro,
    PlanoEnergia,
    Servico,
}

/// <summary>Efeito esperado no desempenho (estimativa honesta, varia de PC para PC).</summary>
public enum Impacto
{
    Baixo,
    Medio,
    Alto,
    Varia,
}

/// <summary>Ação extra depois de gravar/restaurar valores, para o efeito valer na hora.</summary>
public enum AcaoPosterior
{
    Nenhuma,
    AtualizarMouse,
    AvisarMudancaTema,
}

/// <summary>Um valor de registro que o ajuste define.</summary>
/// <param name="Caminho">Formato "HKCU:\..." ou "HKLM:\..." (o mesmo do script PowerShell).</param>
public sealed record ValorAlvo(string Caminho, string Nome, RegistryValueKind Tipo, object Valor);

/// <summary>Definição de um ajuste do catálogo.</summary>
public sealed class Ajuste
{
    /// <summary>Identificador estável. É gravado no backup, então não deve mudar entre versões.</summary>
    public required string Id { get; init; }

    public required string Titulo { get; init; }

    public required string Descricao { get; init; }

    public required string Categoria { get; init; }

    /// <summary>Glifo da fonte Segoe Fluent Icons / Segoe MDL2 Assets.</summary>
    public required string Icone { get; init; }

    public required TipoAjuste Tipo { get; init; }

    public Impacto Impacto { get; init; } = Impacto.Baixo;

    public bool RequerReinicio { get; init; }

    public IReadOnlyList<ValorAlvo> Valores { get; init; } = [];

    /// <summary>Nome do serviço (apenas para <see cref="TipoAjuste.Servico"/>).</summary>
    public string? Servico { get; init; }

    public AcaoPosterior Posterior { get; init; } = AcaoPosterior.Nenhuma;
}

/// <summary>Situação de um ajuste no PC.</summary>
/// <param name="Aplicado">Está no backup ativo (foi aplicado por este programa ou pelo script).</param>
/// <param name="JaNoSistema">O sistema já está com os valores do ajuste, mesmo sem o programa ter aplicado.</param>
public sealed record EstadoAjuste(Ajuste Ajuste, bool Aplicado, bool JaNoSistema);
