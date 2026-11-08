using System.Windows;
using System.Windows.Threading;
using OtimizadorJogos.Core;

namespace OtimizadorJogos;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += AoErroNaoTratado;
        base.OnStartup(e);
    }

    private static void AoErroNaoTratado(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ArmazenamentoBackup.Padrao().Log($"ERRO inesperado: {e.Exception}");
        MessageBox.Show(
            $"Ocorreu um erro inesperado:\n\n{e.Exception.Message}\n\nOs detalhes foram salvos no log.",
            "Otimizador de Jogos",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
