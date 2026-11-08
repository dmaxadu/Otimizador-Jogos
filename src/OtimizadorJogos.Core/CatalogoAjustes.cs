using Microsoft.Win32;

namespace OtimizadorJogos.Core;

/// <summary>
/// Lista de ajustes oferecidos. Os Ids são os mesmos do script scripts/OtimizadorJogos.ps1,
/// então um backup criado pelo script pode ser revertido pelo app (e vice-versa).
/// </summary>
public static class CatalogoAjustes
{
    private const string ChaveGames = @"HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    public static IReadOnlyList<Ajuste> Todos { get; } =
    [
        new Ajuste
        {
            Id = "PlanoEnergia",
            Titulo = "Plano de energia Alto desempenho",
            Descricao = "Mantém o processador em frequência alta durante o jogo, sem economia de energia. "
                      + "Costuma ser o ajuste que mais ajuda em PCs limitados pelo processador.",
            Categoria = "Desempenho",
            Icone = "",
            Tipo = TipoAjuste.PlanoEnergia,
            Impacto = Impacto.Medio,
        },
        new Ajuste
        {
            Id = "GameDVR",
            Titulo = "Gravação em segundo plano desligada",
            Descricao = "Desliga a gravação contínua da Xbox Game Bar, que usa processador e placa de vídeo "
                      + "mesmo quando você não está gravando nada.",
            Categoria = "Desempenho",
            Icone = "",
            Tipo = TipoAjuste.Registro,
            Impacto = Impacto.Medio,
            Valores =
            [
                new(@"HKCU:\System\GameConfigStore", "GameDVR_Enabled", RegistryValueKind.DWord, 0),
                new(@"HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", RegistryValueKind.DWord, 0),
            ],
        },
        new Ajuste
        {
            Id = "ModoJogo",
            Titulo = "Modo de Jogo ligado",
            Descricao = "Dá prioridade ao jogo e segura atualizações e notificações do Windows durante a partida.",
            Categoria = "Desempenho",
            Icone = "",
            Tipo = TipoAjuste.Registro,
            Impacto = Impacto.Baixo,
            Valores =
            [
                new(@"HKCU:\Software\Microsoft\GameBar", "AutoGameModeEnabled", RegistryValueKind.DWord, 1),
                new(@"HKCU:\Software\Microsoft\GameBar", "AllowAutoGameMode", RegistryValueKind.DWord, 1),
            ],
        },
        new Ajuste
        {
            Id = "HAGS",
            Titulo = "Agendamento de GPU por hardware",
            Descricao = "Deixa a placa de vídeo gerenciar a própria memória, tirando trabalho do processador. "
                      + "O efeito varia; se notar travadas, desative este ajuste.",
            Categoria = "Placa de vídeo",
            Icone = "",
            Tipo = TipoAjuste.Registro,
            Impacto = Impacto.Varia,
            RequerReinicio = true,
            Valores =
            [
                new(@"HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", RegistryValueKind.DWord, 2),
            ],
        },
        new Ajuste
        {
            Id = "MouseAcel",
            Titulo = "Aceleração do mouse desligada",
            Descricao = "Desativa o \"Aumentar precisão do ponteiro\": o cursor anda sempre na mesma proporção do "
                      + "movimento da mão. Não muda o FPS; vale para o Windows e jogos sem entrada bruta.",
            Categoria = "Mouse",
            Icone = "",
            Tipo = TipoAjuste.Registro,
            Impacto = Impacto.Baixo,
            Posterior = AcaoPosterior.AtualizarMouse,
            Valores =
            [
                new(@"HKCU:\Control Panel\Mouse", "MouseSpeed", RegistryValueKind.String, "0"),
                new(@"HKCU:\Control Panel\Mouse", "MouseThreshold1", RegistryValueKind.String, "0"),
                new(@"HKCU:\Control Panel\Mouse", "MouseThreshold2", RegistryValueKind.String, "0"),
            ],
        },
        new Ajuste
        {
            Id = "Transparencia",
            Titulo = "Transparência do Windows desligada",
            Descricao = "Remove o efeito translúcido de menus e da barra de tarefas, aliviando um pouco a placa de vídeo.",
            Categoria = "Visual",
            Icone = "",
            Tipo = TipoAjuste.Registro,
            Impacto = Impacto.Baixo,
            Posterior = AcaoPosterior.AvisarMudancaTema,
            Valores =
            [
                new(@"HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", RegistryValueKind.DWord, 0),
            ],
        },
        new Ajuste
        {
            Id = "Animacoes",
            Titulo = "Animações de janelas desligadas",
            Descricao = "Desliga as animações de minimizar, maximizar e da barra de tarefas. "
                      + "Vale por completo depois de sair e entrar na conta.",
            Categoria = "Visual",
            Icone = "",
            Tipo = TipoAjuste.Registro,
            Impacto = Impacto.Baixo,
            RequerReinicio = true,
            Valores =
            [
                new(@"HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", RegistryValueKind.DWord, 0),
                new(@"HKCU:\Control Panel\Desktop\WindowMetrics", "MinAnimate", RegistryValueKind.String, "0"),
            ],
        },
        new Ajuste
        {
            Id = "MMCSS",
            Titulo = "Prioridade multimídia para jogos",
            Descricao = "Ajusta o agendador multimídia do Windows (MMCSS) para dar mais prioridade a jogos. "
                      + "Comum em otimizadores; o efeito costuma ser pequeno.",
            Categoria = "Desempenho",
            Icone = "",
            Tipo = TipoAjuste.Registro,
            Impacto = Impacto.Baixo,
            RequerReinicio = true,
            Valores =
            [
                new(@"HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", RegistryValueKind.DWord, 10),
                new(ChaveGames, "GPU Priority", RegistryValueKind.DWord, 8),
                new(ChaveGames, "Priority", RegistryValueKind.DWord, 6),
                new(ChaveGames, "Scheduling Category", RegistryValueKind.String, "High"),
                new(ChaveGames, "SFIO Priority", RegistryValueKind.String, "High"),
            ],
        },
        new Ajuste
        {
            Id = "Telemetria",
            Titulo = "Telemetria do Windows desligada",
            Descricao = "Desativa o serviço DiagTrack, que coleta e envia dados de uso para a Microsoft "
                      + "em segundo plano. Ganho pequeno.",
            Categoria = "Segundo plano",
            Icone = "",
            Tipo = TipoAjuste.Servico,
            Impacto = Impacto.Baixo,
            Servico = "DiagTrack",
        },
    ];
}
