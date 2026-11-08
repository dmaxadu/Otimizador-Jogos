using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace OtimizadorJogos.Core.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propriedade = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriedade));

    protected bool Set<T>(ref T campo, T valor, [CallerMemberName] string? propriedade = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor))
        {
            return false;
        }

        campo = valor;
        OnPropertyChanged(propriedade);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _executar;
    private readonly Func<bool>? _podeExecutar;

    public RelayCommand(Action executar, Func<bool>? podeExecutar = null)
    {
        _executar = executar;
        _podeExecutar = podeExecutar;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _podeExecutar?.Invoke() ?? true;

    public void Execute(object? parameter) => _executar();

    public void NotificarMudanca() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
