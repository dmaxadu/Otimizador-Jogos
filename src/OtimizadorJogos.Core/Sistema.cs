using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace OtimizadorJogos.Core;

/// <summary>Acesso ao registro. Existe como interface para a lógica poder ser testada sem Windows.</summary>
public interface IRegistro
{
    bool ChaveExiste(string caminho);

    bool TentarLer(string caminho, string nome, out RegistryValueKind tipo, out object? valor);

    void Gravar(string caminho, string nome, RegistryValueKind tipo, object valor);

    void ApagarValor(string caminho, string nome);

    bool ChaveVazia(string caminho);

    void ApagarChave(string caminho);
}

/// <summary>Programas externos e chamadas do Windows.</summary>
public interface ISistema
{
    (int Codigo, string Saida) Executar(string programa, string argumentos, TimeSpan? limite = null);

    void AtualizarMouse(int limiar1, int limiar2, int velocidade);

    void AvisarMudancaTema();

    void AbrirPasta(string pasta);
}

[SupportedOSPlatform("windows")]
public sealed class RegistroWindows : IRegistro
{
    public bool ChaveExiste(string caminho)
    {
        var (raiz, sub) = Abrir(caminho);
        using (raiz)
        using (var chave = raiz.OpenSubKey(sub))
        {
            return chave is not null;
        }
    }

    public bool TentarLer(string caminho, string nome, out RegistryValueKind tipo, out object? valor)
    {
        tipo = RegistryValueKind.Unknown;
        valor = null;
        var (raiz, sub) = Abrir(caminho);
        using (raiz)
        using (var chave = raiz.OpenSubKey(sub))
        {
            if (chave is null || !chave.GetValueNames().Contains(nome, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            tipo = chave.GetValueKind(nome);
            valor = chave.GetValue(nome, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return true;
        }
    }

    public void Gravar(string caminho, string nome, RegistryValueKind tipo, object valor)
    {
        var (raiz, sub) = Abrir(caminho);
        using (raiz)
        using (var chave = raiz.CreateSubKey(sub, writable: true))
        {
            chave.SetValue(nome, valor, tipo);
        }
    }

    public void ApagarValor(string caminho, string nome)
    {
        var (raiz, sub) = Abrir(caminho);
        using (raiz)
        using (var chave = raiz.OpenSubKey(sub, writable: true))
        {
            chave?.DeleteValue(nome, throwOnMissingValue: false);
        }
    }

    public bool ChaveVazia(string caminho)
    {
        var (raiz, sub) = Abrir(caminho);
        using (raiz)
        using (var chave = raiz.OpenSubKey(sub))
        {
            return chave is not null && chave.ValueCount == 0 && chave.SubKeyCount == 0;
        }
    }

    public void ApagarChave(string caminho)
    {
        var (raiz, sub) = Abrir(caminho);
        using (raiz)
        {
            raiz.DeleteSubKey(sub, throwOnMissingSubKey: false);
        }
    }

    private static (RegistryKey Raiz, string Sub) Abrir(string caminho)
    {
        var separador = caminho.IndexOf(":\\", StringComparison.Ordinal);
        if (separador <= 0)
        {
            throw new ArgumentException($"Caminho de registro inválido: {caminho}");
        }

        var hive = caminho[..separador].ToUpperInvariant() switch
        {
            "HKCU" => RegistryHive.CurrentUser,
            "HKLM" => RegistryHive.LocalMachine,
            var outro => throw new ArgumentException($"Raiz de registro não suportada: {outro}"),
        };

        return (RegistryKey.OpenBaseKey(hive, RegistryView.Registry64), caminho[(separador + 2)..]);
    }
}

[SupportedOSPlatform("windows")]
public sealed class SistemaWindows : ISistema
{
    private const uint SpiSetMouse = 0x0004;
    private const uint SpifUpdateIniFileESendChange = 0x03;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr HwndBroadcast = new(0xffff);

    public (int Codigo, string Saida) Executar(string programa, string argumentos, TimeSpan? limite = null)
    {
        var info = new ProcessStartInfo(Resolver(programa), argumentos)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var processo = Process.Start(info)
                             ?? throw new InvalidOperationException($"Não foi possível iniciar {programa}.");
        var erro = processo.StandardError.ReadToEndAsync();
        var saida = processo.StandardOutput.ReadToEndAsync();

        if (!processo.WaitForExit(limite ?? TimeSpan.FromSeconds(60)))
        {
            try
            {
                processo.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException($"{programa} demorou demais para responder.");
        }

        return (processo.ExitCode, saida.GetAwaiter().GetResult() + erro.GetAwaiter().GetResult());
    }

    public void AtualizarMouse(int limiar1, int limiar2, int velocidade)
    {
        var valores = new[] { limiar1, limiar2, velocidade };
        SystemParametersInfo(SpiSetMouse, 0, valores, SpifUpdateIniFileESendChange);
    }

    public void AvisarMudancaTema()
    {
        SendMessageTimeout(HwndBroadcast, WmSettingChange, UIntPtr.Zero, "ImmersiveColorSet", SmtoAbortIfHung, 5000, out _);
    }

    public void AbrirPasta(string pasta)
    {
        Directory.CreateDirectory(pasta);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{pasta}\"") { UseShellExecute = true });
    }

    private static string Resolver(string programa)
    {
        if (Path.IsPathRooted(programa))
        {
            return programa;
        }

        var sistema = Environment.SystemDirectory;
        if (programa.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            var ps = Path.Combine(sistema, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(ps))
            {
                return ps;
            }
        }

        var completo = Path.Combine(sistema, programa);
        return File.Exists(completo) ? completo : programa;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
}

/// <summary>Converte valores de registro para JSON (backup) e de volta.</summary>
public static class ConversorValor
{
    public static JsonElement ParaJson(object valor, RegistryValueKind tipo) => tipo switch
    {
        RegistryValueKind.DWord => JsonSerializer.SerializeToElement(Convert.ToInt32(valor, CultureInfo.InvariantCulture)),
        RegistryValueKind.QWord => JsonSerializer.SerializeToElement(Convert.ToInt64(valor, CultureInfo.InvariantCulture)),
        RegistryValueKind.MultiString => JsonSerializer.SerializeToElement((string[])valor),

        // Bytes como lista de números (igual ao PowerShell), não base64
        RegistryValueKind.Binary => JsonSerializer.SerializeToElement(((byte[])valor).Select(b => (int)b).ToArray()),
        _ => JsonSerializer.SerializeToElement(Convert.ToString(valor, CultureInfo.InvariantCulture) ?? ""),
    };

    public static object DeJson(JsonElement? elemento, RegistryValueKind tipo)
    {
        if (elemento is not { } e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidDataException("O valor original está faltando no backup.");
        }

        return tipo switch
        {
            RegistryValueKind.DWord => unchecked((int)LerInteiro(e)),
            RegistryValueKind.QWord => LerInteiro(e),
            RegistryValueKind.MultiString => e.ValueKind == JsonValueKind.Array
                ? e.EnumerateArray().Select(x => x.ToString()).ToArray()
                : new[] { e.ToString() },
            RegistryValueKind.Binary => e.ValueKind == JsonValueKind.Array
                ? e.EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray()
                : new[] { (byte)e.GetInt32() },
            _ => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText(),
        };
    }

    public static string Texto(object? valor) => Convert.ToString(valor, CultureInfo.InvariantCulture) ?? "";

    private static long LerInteiro(JsonElement e) =>
        e.ValueKind == JsonValueKind.String
            ? long.Parse(e.GetString() ?? "0", CultureInfo.InvariantCulture)
            : e.GetInt64();
}
