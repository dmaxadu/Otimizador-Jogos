@echo off
title Otimizador de Jogos
cd /d "%~dp0"

if not exist "%~dp0OtimizadorJogos.ps1" (
    echo Nao encontrei o arquivo OtimizadorJogos.ps1 nesta pasta.
    echo Deixe o Executar.bat e o OtimizadorJogos.ps1 juntos na mesma pasta.
    echo.
    pause
    exit /b 1
)

rem Remove o bloqueio de "arquivo baixado da internet" do script
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Unblock-File -LiteralPath '%~dp0OtimizadorJogos.ps1'" >nul 2>&1

rem Roda o script (ele mesmo pede permissao de administrador e abre em outra janela)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0OtimizadorJogos.ps1"

echo.
echo Se apareceu algum erro em vermelho acima, tire um print e mande para o Claude.
pause
