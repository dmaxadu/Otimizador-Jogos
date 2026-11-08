using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using OtimizadorJogos.Core;
using OtimizadorJogos.Core.ViewModels;

namespace OtimizadorJogos;

public partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeAntigo = 19;

    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        var armazenamento = ArmazenamentoBackup.Padrao();
        var sistema = new SistemaWindows();
        var otimizador = new Otimizador(new RegistroWindows(), sistema, armazenamento);

        _vm = new MainViewModel(otimizador, armazenamento, sistema)
        {
            Confirmar = (titulo, mensagem) =>
                MessageBox.Show(this, mensagem, titulo, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes,
            MostrarMensagem = (titulo, mensagem) =>
                MessageBox.Show(this, mensagem, titulo, MessageBoxButton.OK, MessageBoxImage.Warning),
        };

        DataContext = _vm;
        SourceInitialized += (_, _) => AtivarBarraDeTituloEscura();
        Loaded += async (_, _) => await _vm.CarregarAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Não deixa fechar no meio de uma operação
        if (_vm.Ocupado)
        {
            e.Cancel = true;
            return;
        }

        if (_vm.Pendentes > 0)
        {
            var resposta = MessageBox.Show(
                this,
                "Há alterações que ainda não foram aplicadas. Deseja sair mesmo assim?",
                "Alterações pendentes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            e.Cancel = resposta != MessageBoxResult.Yes;
        }

        base.OnClosing(e);
    }

    private void AtivarBarraDeTituloEscura()
    {
        var janela = new WindowInteropHelper(this).Handle;
        var ativar = 1;
        if (DwmSetWindowAttribute(janela, DwmwaUseImmersiveDarkMode, ref ativar, sizeof(int)) != 0)
        {
            // Versões antigas do Windows 10 usam outro código
            DwmSetWindowAttribute(janela, DwmwaUseImmersiveDarkModeAntigo, ref ativar, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamanho);
}
