<#
.SYNOPSIS
    Otimizador de Jogos v1.0 - aplica ajustes do Windows para jogos e permite reverter tudo.

.DESCRIPTION
    Antes de alterar qualquer coisa, o script salva o valor original de cada ajuste em
    C:\ProgramData\OtimizadorJogos\backup.json. A opção "Reverter" usa esse arquivo para
    devolver tudo exatamente ao estado anterior (inclusive apagando valores e chaves de
    registro que não existiam antes).

    Também tenta criar um ponto de restauração do Windows antes de aplicar (segurança extra).

    Não mexe em nada do jogo nem do anti-cheat (Vanguard): só configurações do Windows.

.PARAMETER Acao
    Aplicar | Reverter | Status. Sem esse parâmetro, abre um menu interativo.

.PARAMETER SemPausa
    Não espera Enter no final (útil para rodar de dentro de um terminal já aberto).

.EXAMPLE
    Clique com o botão direito no arquivo -> "Executar com o PowerShell"

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\OtimizadorJogos.ps1 -Acao Reverter

.NOTES
    Rode logado na SUA conta do Windows (a que você usa para jogar) e apenas confirme o
    pedido de administrador. Vários ajustes ficam no perfil do usuário atual (HKCU).

    Para escolher quais ajustes aplicar, mude "Ativo = $true" para "Ativo = $false"
    na lista $Ajustes mais abaixo.
#>

[CmdletBinding()]
param(
    [ValidateSet('Aplicar', 'Reverter', 'Status')]
    [string]$Acao,

    [switch]$SemPausa
)

$ErrorActionPreference = 'Stop'
$Versao = '1.1'

# Qualquer erro não tratado: mostra a mensagem e espera Enter, em vez de fechar a janela
trap {
    Write-Host ''
    Write-Host "ERRO: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Linha $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim())" -ForegroundColor DarkGray
    try { Stop-Transcript | Out-Null } catch { }
    Read-Host 'Pressione Enter para sair' | Out-Null
    exit 1
}

# O Windows pode rodar scripts baixados da internet em modo restrito (Smart App Control / bloqueio)
if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') {
    Write-Host 'O Windows está rodando este script em modo restrito, então ele não pode continuar.' -ForegroundColor Red
    Write-Host 'Clique com o botão direito no arquivo -> Propriedades -> marque "Desbloquear" -> OK, e tente de novo.'
    Write-Host 'Se continuar, o Controle Inteligente de Aplicativos (Smart App Control) pode estar bloqueando scripts.'
    Read-Host 'Pressione Enter para sair' | Out-Null
    exit 1
}
$PastaDados = Join-Path $env:ProgramData 'OtimizadorJogos'
$ArquivoBackup = Join-Path $PastaDados 'backup.json'
$ArquivoLog = Join-Path $PastaDados 'log.txt'

# ---------------------------------------------------------------------------
# Elevação: se não estiver como administrador, reabre pedindo permissão
# ---------------------------------------------------------------------------
function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $id
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    Write-Host 'Pedindo permissão de administrador...' -ForegroundColor Cyan
    $argumentos = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($Acao) { $argumentos += @('-Acao', $Acao) }
    try {
        Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $argumentos -Verb RunAs
    }
    catch {
        Write-Host 'Permissão de administrador negada. Nada foi alterado.' -ForegroundColor Red
        if (-not $SemPausa) { Read-Host 'Pressione Enter para sair' | Out-Null }
    }
    exit
}

# ---------------------------------------------------------------------------
# Funções nativas do Windows (para aplicar mouse e transparência na hora)
# ---------------------------------------------------------------------------
if (-not ('OtimizadorJogos.Nativo' -as [type])) {
    Add-Type -Namespace OtimizadorJogos -Name Nativo -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true)]
public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam,
    uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
'@
}

$AtualizarMouse = {
    # Reaplica os valores do registro na sessão atual (SPI_SETMOUSE), sem precisar sair da conta
    $p = Get-ItemProperty -LiteralPath 'HKCU:\Control Panel\Mouse'
    $valores = [int[]]@([int]$p.MouseThreshold1, [int]$p.MouseThreshold2, [int]$p.MouseSpeed)
    [void][OtimizadorJogos.Nativo]::SystemParametersInfo(0x0004, 0, $valores, 0x03)
}

$AvisarMudancaTema = {
    # Avisa o Windows que as configurações de cor/transparência mudaram (WM_SETTINGCHANGE)
    $resultado = [UIntPtr]::Zero
    [void][OtimizadorJogos.Nativo]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero,
        'ImmersiveColorSet', 2, 5000, [ref]$resultado)
}

# ---------------------------------------------------------------------------
# Lista de ajustes. Mude Ativo para $false para pular um ajuste.
# ---------------------------------------------------------------------------
$GuidAltoDesempenho = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
$ChaveGames = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games'

$Ajustes = @(
    [pscustomobject]@{
        Id    = 'PlanoEnergia'
        Nome  = 'Plano de energia "Alto desempenho"'
        Ativo = $true
        Tipo  = 'PlanoEnergia'
        Nota  = 'Evita que o processador reduza a frequência durante o jogo.'
    }
    [pscustomobject]@{
        Id      = 'GameDVR'
        Nome    = 'Desligar gravação em segundo plano (Game DVR / Xbox Game Bar)'
        Ativo   = $true
        Tipo    = 'Registro'
        Nota    = 'A gravação contínua usa CPU e GPU mesmo sem você gravar nada.'
        Valores = @(
            @{ Caminho = 'HKCU:\System\GameConfigStore'; Nome = 'GameDVR_Enabled'; Tipo = 'DWord'; Valor = 0 }
            @{ Caminho = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR'; Nome = 'AppCaptureEnabled'; Tipo = 'DWord'; Valor = 0 }
        )
    }
    [pscustomobject]@{
        Id      = 'ModoJogo'
        Nome    = 'Ligar o Modo de Jogo do Windows'
        Ativo   = $true
        Tipo    = 'Registro'
        Nota    = 'Dá prioridade ao jogo e segura atualizações do Windows durante a partida.'
        Valores = @(
            @{ Caminho = 'HKCU:\Software\Microsoft\GameBar'; Nome = 'AutoGameModeEnabled'; Tipo = 'DWord'; Valor = 1 }
            @{ Caminho = 'HKCU:\Software\Microsoft\GameBar'; Nome = 'AllowAutoGameMode'; Tipo = 'DWord'; Valor = 1 }
        )
    }
    [pscustomobject]@{
        Id      = 'HAGS'
        Nome    = 'Ligar agendamento de GPU acelerado por hardware'
        Ativo   = $true
        Tipo    = 'Registro'
        Nota    = 'Precisa reiniciar o PC. O efeito varia; se notar travadas, desligue este ajuste.'
        Valores = @(
            @{ Caminho = 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers'; Nome = 'HwSchMode'; Tipo = 'DWord'; Valor = 2 }
        )
    }
    [pscustomobject]@{
        Id      = 'MouseAcel'
        Nome    = 'Desligar aceleração do mouse ("Aumentar precisão do ponteiro")'
        Ativo   = $true
        Tipo    = 'Registro'
        Nota    = 'Vale para o Windows e para jogos que não usam entrada bruta (raw input).'
        Depois  = $AtualizarMouse
        Valores = @(
            @{ Caminho = 'HKCU:\Control Panel\Mouse'; Nome = 'MouseSpeed'; Tipo = 'String'; Valor = '0' }
            @{ Caminho = 'HKCU:\Control Panel\Mouse'; Nome = 'MouseThreshold1'; Tipo = 'String'; Valor = '0' }
            @{ Caminho = 'HKCU:\Control Panel\Mouse'; Nome = 'MouseThreshold2'; Tipo = 'String'; Valor = '0' }
        )
    }
    [pscustomobject]@{
        Id      = 'Transparencia'
        Nome    = 'Desligar efeitos de transparência do Windows'
        Ativo   = $true
        Tipo    = 'Registro'
        Nota    = 'Alivia um pouco a GPU com o jogo em segundo plano ou em janela.'
        Depois  = $AvisarMudancaTema
        Valores = @(
            @{ Caminho = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'; Nome = 'EnableTransparency'; Tipo = 'DWord'; Valor = 0 }
        )
    }
    [pscustomobject]@{
        Id      = 'Animacoes'
        Nome    = 'Desligar animações de janelas e da barra de tarefas'
        Ativo   = $true
        Tipo    = 'Registro'
        Nota    = 'Vale totalmente depois de sair e entrar na conta. Ganho pequeno.'
        Valores = @(
            @{ Caminho = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'; Nome = 'TaskbarAnimations'; Tipo = 'DWord'; Valor = 0 }
            @{ Caminho = 'HKCU:\Control Panel\Desktop\WindowMetrics'; Nome = 'MinAnimate'; Tipo = 'String'; Valor = '0' }
        )
    }
    [pscustomobject]@{
        Id      = 'MMCSS'
        Nome    = 'Prioridade multimídia para jogos (MMCSS)'
        Ativo   = $true
        Tipo    = 'Registro'
        Nota    = 'Ajuste comum nesses programas. Efeito pequeno e varia de PC para PC.'
        Valores = @(
            @{ Caminho = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'; Nome = 'SystemResponsiveness'; Tipo = 'DWord'; Valor = 10 }
            @{ Caminho = $ChaveGames; Nome = 'GPU Priority'; Tipo = 'DWord'; Valor = 8 }
            @{ Caminho = $ChaveGames; Nome = 'Priority'; Tipo = 'DWord'; Valor = 6 }
            @{ Caminho = $ChaveGames; Nome = 'Scheduling Category'; Tipo = 'String'; Valor = 'High' }
            @{ Caminho = $ChaveGames; Nome = 'SFIO Priority'; Tipo = 'String'; Valor = 'High' }
        )
    }
    [pscustomobject]@{
        Id      = 'Telemetria'
        Nome    = 'Desligar serviço de telemetria (DiagTrack)'
        Ativo   = $true
        Tipo    = 'Servico'
        Servico = 'DiagTrack'
        Nota    = 'Menos atividade em segundo plano. Ganho pequeno.'
    }
)

# ---------------------------------------------------------------------------
# Mensagens
# ---------------------------------------------------------------------------
function Write-Ok([string]$m) { Write-Host "  [OK]   $m" -ForegroundColor Green }
function Write-Aviso([string]$m) { Write-Host "  [!]    $m" -ForegroundColor Yellow }
function Write-Erro([string]$m) { Write-Host "  [ERRO] $m" -ForegroundColor Red }
function Write-Info([string]$m) { Write-Host "         $m" -ForegroundColor DarkGray }

# ---------------------------------------------------------------------------
# Registro: ler estado, gravar e restaurar
# ---------------------------------------------------------------------------
function Get-EstadoRegistro([string]$Caminho, [string]$Nome) {
    $estado = [ordered]@{
        Caminho      = $Caminho
        Nome         = $Nome
        ChaveExistia = $false
        ValorExistia = $false
        Tipo         = $null
        Valor        = $null
    }
    if (Test-Path -LiteralPath $Caminho) {
        $estado.ChaveExistia = $true
        $chave = Get-Item -LiteralPath $Caminho
        if ($chave.GetValueNames() -contains $Nome) {
            $estado.ValorExistia = $true
            $estado.Tipo = $chave.GetValueKind($Nome).ToString()
            $estado.Valor = $chave.GetValue($Nome, $null, 'DoNotExpandEnvironmentNames')
        }
    }
    return $estado
}

function Set-ValorRegistro([string]$Caminho, [string]$Nome, [string]$Tipo, $Valor) {
    if (-not (Test-Path -LiteralPath $Caminho)) {
        New-Item -Path $Caminho -Force | Out-Null
    }
    New-ItemProperty -LiteralPath $Caminho -Name $Nome -PropertyType $Tipo -Value $Valor -Force | Out-Null
}

function Restore-ValorRegistro($Estado) {
    if ($Estado.ValorExistia) {
        $valor = $Estado.Valor
        switch ($Estado.Tipo) {
            'Binary' { $valor = [byte[]]@($valor) }
            'MultiString' { $valor = [string[]]@($valor) }
        }
        Set-ValorRegistro $Estado.Caminho $Estado.Nome $Estado.Tipo $valor
    }
    elseif (Test-Path -LiteralPath $Estado.Caminho) {
        # O valor não existia antes: apaga
        $chave = Get-Item -LiteralPath $Estado.Caminho
        if ($chave.GetValueNames() -contains $Estado.Nome) {
            Remove-ItemProperty -LiteralPath $Estado.Caminho -Name $Estado.Nome
        }
        # A chave também não existia e ficou vazia: apaga
        if (-not $Estado.ChaveExistia) {
            $chave = Get-Item -LiteralPath $Estado.Caminho
            if ($chave.ValueCount -eq 0 -and $chave.SubKeyCount -eq 0) {
                Remove-Item -LiteralPath $Estado.Caminho
            }
        }
    }
}

# ---------------------------------------------------------------------------
# Plano de energia
# ---------------------------------------------------------------------------
$RegexGuid = '[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}'

function Get-PlanoAtivo {
    $saida = (powercfg /getactivescheme) | Out-String
    if ($saida -match $RegexGuid) { return $Matches[0].ToLower() }
    throw 'Não foi possível ler o plano de energia ativo.'
}

function Test-PlanoExiste([string]$Guid) {
    $lista = (powercfg /list) | Out-String
    return ($lista -match [regex]::Escape($Guid))
}

# ---------------------------------------------------------------------------
# Serviços
# ---------------------------------------------------------------------------
function Get-EstadoServico([string]$Nome) {
    $svc = Get-Service -Name $Nome -ErrorAction SilentlyContinue
    if (-not $svc) { return $null }
    $p = Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$Nome"
    return [ordered]@{
        Nome     = $Nome
        Start    = [int]$p.Start
        Atrasado = ($p.DelayedAutostart -eq 1)
        Rodando  = ($svc.Status -eq 'Running')
    }
}

function Get-ModoInicioSc($Estado) {
    switch ([int]$Estado.Start) {
        2 { if ($Estado.Atrasado) { return 'delayed-auto' } else { return 'auto' } }
        3 { return 'demand' }
        4 { return 'disabled' }
        default { return 'demand' }
    }
}

# ---------------------------------------------------------------------------
# Backup
# ---------------------------------------------------------------------------
function Save-Backup($Backup) {
    $Backup | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ArquivoBackup -Encoding UTF8
}

# ---------------------------------------------------------------------------
# APLICAR
# ---------------------------------------------------------------------------
function Invoke-Aplicar {
    if (Test-Path -LiteralPath $ArquivoBackup) {
        Write-Aviso 'Os ajustes já estão aplicados (existe um backup ativo).'
        Write-Info  'Reverta primeiro se quiser aplicar de novo. Isso protege o backup original.'
        return
    }

    $ativos = @($Ajustes | Where-Object { $_.Ativo })
    if ($ativos.Count -eq 0) {
        Write-Aviso 'Nenhum ajuste está marcado como Ativo no script.'
        return
    }

    Write-Host ''
    Write-Host 'Estes ajustes serão aplicados:' -ForegroundColor Cyan
    foreach ($a in $ativos) {
        Write-Host "  - $($a.Nome)"
        Write-Info $a.Nota
    }
    Write-Host ''
    $resposta = Read-Host 'Continuar? (S/N)'
    if ($resposta -notmatch '^[sS]') {
        Write-Host 'Cancelado. Nada foi alterado.'
        return
    }

    New-Item -ItemType Directory -Path $PastaDados -Force | Out-Null

    # Ponto de restauração (segurança extra; o backup próprio não depende dele)
    Write-Host ''
    if (Get-Command Checkpoint-Computer -ErrorAction SilentlyContinue) {
        Write-Host 'Criando ponto de restauração do Windows...' -ForegroundColor Cyan
        try {
            $avisos = $null
            Checkpoint-Computer -Description 'Antes do Otimizador de Jogos' -RestorePointType 'MODIFY_SETTINGS' `
                -WarningVariable avisos -WarningAction SilentlyContinue
            if ($avisos) {
                Write-Aviso 'O Windows não criou um ponto novo (ele só permite 1 a cada 24 h).'
                Write-Info  'Sem problema: o backup próprio do script cobre a reversão.'
            }
            else {
                Write-Ok 'Ponto de restauração criado.'
            }
        }
        catch {
            Write-Aviso 'Não deu para criar o ponto de restauração (talvez a Restauração do Sistema esteja desligada).'
            Write-Info  'Sem problema: o backup próprio do script cobre a reversão.'
        }
    }

    $backup = [ordered]@{
        Versao  = $Versao
        Data    = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        Usuario = $env:USERNAME
        Itens   = New-Object System.Collections.Generic.List[object]
    }
    Save-Backup $backup

    Write-Host ''
    Write-Host 'Aplicando...' -ForegroundColor Cyan
    $erros = 0

    foreach ($a in $ativos) {
        $pulado = $false
        try {
            switch ($a.Tipo) {
                'Registro' {
                    # 1) guarda o estado original de TODOS os valores, 2) salva o backup, 3) altera
                    $estados = @(foreach ($v in $a.Valores) { Get-EstadoRegistro $v.Caminho $v.Nome })
                    $backup.Itens.Add([ordered]@{ Id = $a.Id; Tipo = 'Registro'; Estados = $estados })
                    Save-Backup $backup
                    foreach ($v in $a.Valores) {
                        Set-ValorRegistro $v.Caminho $v.Nome $v.Tipo $v.Valor
                    }
                    if ($a.Depois) { & $a.Depois }
                }

                'PlanoEnergia' {
                    $item = [ordered]@{ Id = $a.Id; Tipo = 'PlanoEnergia'; PlanoAnterior = (Get-PlanoAtivo); PlanoCriado = $null }
                    $backup.Itens.Add($item)
                    Save-Backup $backup

                    $alvo = $GuidAltoDesempenho
                    if (-not (Test-PlanoExiste $alvo)) {
                        # Plano escondido no Windows: cria uma cópia (e anota para apagar ao reverter)
                        $saida = (powercfg -duplicatescheme $GuidAltoDesempenho) | Out-String
                        if ($saida -notmatch $RegexGuid) { throw 'Não foi possível criar o plano Alto desempenho.' }
                        $alvo = $Matches[0].ToLower()
                        $item.PlanoCriado = $alvo
                        Save-Backup $backup
                    }
                    powercfg /setactive $alvo | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw "powercfg retornou código $LASTEXITCODE." }
                }

                'Servico' {
                    $estado = Get-EstadoServico $a.Servico
                    if (-not $estado) {
                        Write-Aviso "$($a.Nome): serviço não encontrado neste Windows, pulando."
                        $pulado = $true
                    }
                    else {
                        $backup.Itens.Add([ordered]@{ Id = $a.Id; Tipo = 'Servico'; Estado = $estado })
                        Save-Backup $backup
                        if ($estado.Rodando) { Stop-Service -Name $a.Servico -Force }
                        Set-Service -Name $a.Servico -StartupType Disabled
                    }
                }
            }
            if (-not $pulado) { Write-Ok $a.Nome }
        }
        catch {
            $erros++
            Write-Erro "$($a.Nome): $($_.Exception.Message)"
        }
    }

    Write-Host ''
    if ($erros -eq 0) {
        Write-Host 'Pronto! Todos os ajustes foram aplicados.' -ForegroundColor Green
    }
    else {
        Write-Aviso "Terminou com $erros erro(s). O que foi alterado está no backup e pode ser revertido."
    }
    Write-Host "Backup salvo em: $ArquivoBackup"
    Write-Host 'Reinicie o PC para todos os ajustes valerem.' -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------
# REVERTER
# ---------------------------------------------------------------------------
function Invoke-Reverter {
    if (-not (Test-Path -LiteralPath $ArquivoBackup)) {
        Write-Aviso 'Não há backup ativo: nada para reverter.'
        return
    }

    $dados = Get-Content -LiteralPath $ArquivoBackup -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host ''
    Write-Host "Revertendo os ajustes aplicados em $($dados.Data)..." -ForegroundColor Cyan

    # Desfaz na ordem inversa da aplicação
    $itens = @($dados.Itens)
    [array]::Reverse($itens)
    $erros = 0

    foreach ($item in $itens) {
        $def = $Ajustes | Where-Object { $_.Id -eq $item.Id } | Select-Object -First 1
        $nome = if ($def) { $def.Nome } else { $item.Id }
        try {
            switch ($item.Tipo) {
                'Registro' {
                    $estados = @($item.Estados)
                    [array]::Reverse($estados)
                    foreach ($e in $estados) { Restore-ValorRegistro $e }
                    if ($def -and $def.Depois) { & $def.Depois }
                }

                'PlanoEnergia' {
                    powercfg /setactive $item.PlanoAnterior | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw "powercfg retornou código $LASTEXITCODE." }
                    if ($item.PlanoCriado -and (Test-PlanoExiste $item.PlanoCriado)) {
                        powercfg /delete $item.PlanoCriado | Out-Null
                    }
                }

                'Servico' {
                    $e = $item.Estado
                    $modo = Get-ModoInicioSc $e
                    $saida = sc.exe config $e.Nome start= $modo
                    if ($LASTEXITCODE -ne 0) { throw "sc.exe: $saida" }
                    if ($e.Rodando) { Start-Service -Name $e.Nome }
                }
            }
            Write-Ok "Revertido: $nome"
        }
        catch {
            $erros++
            Write-Erro "$($nome): $($_.Exception.Message)"
        }
    }

    Write-Host ''
    if ($erros -eq 0) {
        $arquivado = Join-Path $PastaDados ("backup-revertido-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        Move-Item -LiteralPath $ArquivoBackup -Destination $arquivado
        Write-Host 'Tudo revertido ao estado original.' -ForegroundColor Green
        Write-Info "Backup arquivado em: $arquivado"
    }
    else {
        Write-Aviso "Terminou com $erros erro(s). O backup foi mantido; rode Reverter de novo depois de reiniciar."
    }
    Write-Host 'Reinicie o PC para concluir.' -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------
# STATUS
# ---------------------------------------------------------------------------
function Show-Status {
    Write-Host ''
    $planoCriado = $null
    if (Test-Path -LiteralPath $ArquivoBackup) {
        $dados = Get-Content -LiteralPath $ArquivoBackup -Raw -Encoding UTF8 | ConvertFrom-Json
        Write-Host "Ajustes aplicados em $($dados.Data) (backup ativo)." -ForegroundColor Cyan
        $p = @($dados.Itens) | Where-Object { $_.Tipo -eq 'PlanoEnergia' } | Select-Object -First 1
        if ($p) { $planoCriado = $p.PlanoCriado }
    }
    else {
        Write-Host 'Nenhum backup ativo (o script não está aplicado).' -ForegroundColor Cyan
    }
    Write-Host ''

    foreach ($a in $Ajustes) {
        $aplicado = $false
        try {
            switch ($a.Tipo) {
                'Registro' {
                    $aplicado = $true
                    foreach ($v in $a.Valores) {
                        $e = Get-EstadoRegistro $v.Caminho $v.Nome
                        if (-not $e.ValorExistia -or ("$($e.Valor)" -ne "$($v.Valor)")) { $aplicado = $false }
                    }
                }
                'PlanoEnergia' {
                    $ativo = Get-PlanoAtivo
                    $aplicado = ($ativo -eq $GuidAltoDesempenho) -or ($planoCriado -and $ativo -eq $planoCriado)
                }
                'Servico' {
                    $svc = Get-Service -Name $a.Servico -ErrorAction SilentlyContinue
                    $aplicado = ($svc -and $svc.StartType -eq 'Disabled')
                }
            }
        }
        catch { $aplicado = $false }

        if ($aplicado) { Write-Host "  [aplicado] $($a.Nome)" -ForegroundColor Green }
        else { Write-Host "  [original] $($a.Nome)" -ForegroundColor Gray }
    }
}

# ---------------------------------------------------------------------------
# Execução
# ---------------------------------------------------------------------------
New-Item -ItemType Directory -Path $PastaDados -Force | Out-Null
try { Start-Transcript -LiteralPath $ArquivoLog -Append | Out-Null } catch { }

Write-Host ''
Write-Host "=== Otimizador de Jogos v$Versao ===" -ForegroundColor Cyan

if ($Acao) {
    switch ($Acao) {
        'Aplicar' { Invoke-Aplicar }
        'Reverter' { Invoke-Reverter }
        'Status' { Show-Status }
    }
}
else {
    do {
        Write-Host ''
        Write-Host '1) Aplicar ajustes'
        Write-Host '2) Reverter tudo'
        Write-Host '3) Ver status'
        Write-Host '4) Sair'
        $opcao = Read-Host 'Escolha'
        switch ($opcao) {
            '1' { Invoke-Aplicar }
            '2' { Invoke-Reverter }
            '3' { Show-Status }
        }
    } while ($opcao -ne '4')
    $SemPausa = $true
}

try { Stop-Transcript | Out-Null } catch { }
if (-not $SemPausa) {
    Write-Host ''
    Read-Host 'Pressione Enter para sair' | Out-Null
}
