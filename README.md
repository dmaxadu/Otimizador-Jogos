<p align="center">
  <img src="docs/icone.png" width="96" alt="Ícone do Otimizador de Jogos">
</p>

<h1 align="center">Otimizador de Jogos</h1>

<p align="center">
  Ajustes do Windows para jogar com mais FPS e menos travadas.<br>
  <b>Tudo transparente, com backup automático e reversão com um clique.</b>
</p>

<p align="center">
  <a href="../../releases/latest">Baixar a última versão</a> ·
  <a href="#ajustes">Ver os ajustes</a> ·
  <a href="#como-a-reversão-funciona">Como a reversão funciona</a>
</p>

<!-- Depois de rodar o app, tire um print, salve em docs/screenshot.png e descomente a linha abaixo -->
<!-- <p align="center"><img src="docs/screenshot.png" width="760" alt="Tela do Otimizador de Jogos"></p> -->

---

## Por que dá para confiar

Muitos "otimizadores" pagos aplicam dezenas de ajustes sem dizer quais, pedem acesso remoto ao seu PC ou desligam proteções do Windows. Este projeto faz o contrário:

- **Backup antes de cada mudança.** O valor original de cada configuração é salvo *antes* de ser alterado. Se o app fechar no meio, o que foi mexido continua no backup.
- **Reversão total ou por ajuste.** Desligue um interruptor e aplique para desfazer só aquele ajuste, ou use **Restaurar tudo** para voltar o Windows exatamente como estava, apagando inclusive valores e chaves de registro que não existiam antes.
- **Ponto de restauração do Windows** criado antes da primeira alteração, como segurança extra.
- **Nada no jogo nem no anti-cheat.** Só configurações do Windows. Nenhum arquivo do jogo é tocado e nada é injetado em processos (seguro com o Vanguard, o VAC e afins).
- **Sem internet.** O app não se conecta a nada nem coleta dados.
- **Honesto sobre o efeito.** Cada ajuste mostra o impacto esperado. Ajustes populares que quase não mudam nada estão marcados como "impacto baixo", e ajustes de rede que são placebo nem entraram.
- **Código aberto.** Tudo o que o app faz está em [`CatalogoAjustes.cs`](src/OtimizadorJogos.Core/CatalogoAjustes.cs) e [`Otimizador.cs`](src/OtimizadorJogos.Core/Otimizador.cs).

## Ajustes

| Ajuste | O que muda | Impacto | Reinício |
|---|---|---|---|
| Plano de energia Alto desempenho | Processador sem economia de energia durante o jogo | Médio | Não |
| Gravação em segundo plano desligada | Desliga a gravação contínua da Xbox Game Bar (Game DVR) | Médio | Não |
| Modo de Jogo ligado | Prioridade ao jogo e menos interrupções do Windows | Baixo | Não |
| Agendamento de GPU por hardware | A placa de vídeo gerencia a própria memória | Variável | Sim |
| Aceleração do mouse desligada | Desliga "Aumentar precisão do ponteiro" (não muda FPS) | Baixo | Não |
| Transparência desligada | Remove o efeito translúcido de menus e barra de tarefas | Baixo | Não |
| Animações de janelas desligadas | Sem animações de minimizar/maximizar e da barra de tarefas | Baixo | Sim |
| Prioridade multimídia para jogos | Ajusta o agendador multimídia (MMCSS) para jogos | Baixo | Sim |
| Telemetria desligada | Desativa o serviço DiagTrack | Baixo | Não |

> O ganho real depende do PC. Para saber se valeu a pena, meça o FPS antes e depois, sempre no mesmo cenário (por exemplo, o Campo de Treino do Valorant com as estatísticas de FPS ligadas).

## Como usar

1. Baixe o `OtimizadorJogos.exe` na [página de releases](../../releases/latest). Não precisa instalar nada.
2. Abra e aceite o pedido de administrador. Use a mesma conta do Windows em que você joga, porque vários ajustes ficam no perfil do usuário.
3. Ligue os ajustes que quiser e clique em **Aplicar alterações**.
4. Reinicie o PC.

Como o executável não tem assinatura digital paga, o Windows pode mostrar "O Windows protegeu o computador". Clique em **Mais informações → Executar assim mesmo**. Se preferir, compile você mesmo (veja abaixo).

## Como a reversão funciona

O backup fica em `C:\ProgramData\OtimizadorJogos\backup.json`. Para cada ajuste aplicado, ele guarda:

- **Registro:** se a chave e o valor existiam, o tipo e o valor original.
- **Plano de energia:** qual plano estava ativo e, se o app precisou criar uma cópia do "Alto desempenho", qual foi, para apagá-la ao reverter.
- **Serviço:** o tipo de inicialização original (automático, automático atrasado ou manual) e se estava rodando.

Ao reverter, tudo é restaurado na ordem inversa. Quando não sobra nenhum ajuste aplicado, o backup é arquivado como `backup-revertido-<data>.json`. Tudo o que acontece fica registrado em `log.txt`, na mesma pasta.

O formato do backup é o mesmo da [versão em script](scripts/): o que um aplica, o outro consegue reverter.

## Compilar a partir do código

Requisitos: Windows 10/11 e o [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/<seu-usuario>/otimizador-jogos.git
cd otimizador-jogos
dotnet build -c Release

# Executável único, sem depender do .NET instalado
dotnet publish src/OtimizadorJogos/OtimizadorJogos.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

O app exige administrador (pelo manifesto), então rode o `.exe` gerado, ou abra o terminal ou o Visual Studio como administrador para usar `dotnet run`.

O GitHub Actions compila a cada push. Ao criar uma tag `v*` (por exemplo, `git tag v1.0.0 && git push --tags`), ele publica uma release com o `.exe` pronto.

## Estrutura do projeto

```
src/
  OtimizadorJogos.Core/      Lógica: catálogo de ajustes, backup, registro, plano de energia, serviços, ViewModels
  OtimizadorJogos/           Interface WPF (tema escuro, sem dependências externas)
scripts/
  OtimizadorJogos.ps1        Versão em PowerShell com menu (aplicar, reverter, status)
  Executar.bat               Atalho para rodar o script sem problemas de política de execução
.github/workflows/build.yml  Build e release automáticos
```

A lógica fica separada da interface e depende de interfaces (`IRegistro`, `ISistema`), então pode ser testada sem mexer no Windows de verdade.

## Versão em script

Prefere não usar um executável? A pasta [`scripts/`](scripts/) tem a mesma lógica em PowerShell. Coloque os dois arquivos na mesma pasta e dê dois cliques no `Executar.bat`.

## Aviso

Este software é fornecido "como está", sem garantias. Todos os ajustes são configurações documentadas do próprio Windows e podem ser revertidos pelo app, mas use por sua conta e risco.

## Licença

[MIT](LICENSE)
