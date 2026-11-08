using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OtimizadorJogos.Core;

// O formato do backup é o mesmo gravado pelo script PowerShell (scripts/OtimizadorJogos.ps1).
// Os nomes das propriedades não devem mudar.

public sealed class Backup
{
    public string Versao { get; set; } = "";

    public string Data { get; set; } = "";

    public string Usuario { get; set; } = "";

    [JsonConverter(typeof(ListaOuUnicoConverter<ItemBackup>))]
    public List<ItemBackup> Itens { get; set; } = [];
}

public sealed class ItemBackup
{
    public string Id { get; set; } = "";

    /// <summary>"Registro", "PlanoEnergia" ou "Servico".</summary>
    public string Tipo { get; set; } = "";

    [JsonConverter(typeof(ListaOuUnicoConverter<EstadoRegistro>))]
    public List<EstadoRegistro>? Estados { get; set; }

    public string? PlanoAnterior { get; set; }

    public string? PlanoCriado { get; set; }

    public EstadoServico? Estado { get; set; }
}

/// <summary>Estado original de um valor de registro, antes do ajuste.</summary>
public sealed class EstadoRegistro
{
    public string Caminho { get; set; } = "";

    public string Nome { get; set; } = "";

    public bool ChaveExistia { get; set; }

    public bool ValorExistia { get; set; }

    /// <summary>Nome de <see cref="Microsoft.Win32.RegistryValueKind"/> (ex.: "DWord", "String").</summary>
    public string? Tipo { get; set; }

    public JsonElement? Valor { get; set; }
}

/// <summary>Estado original de um serviço do Windows.</summary>
public sealed class EstadoServico
{
    public string Nome { get; set; } = "";

    /// <summary>2 = automático, 3 = manual, 4 = desativado.</summary>
    public int Start { get; set; }

    public bool Atrasado { get; set; }

    public bool Rodando { get; set; }
}

/// <summary>Aceita tanto uma lista JSON quanto um objeto único (o PowerShell às vezes "desembrulha" listas de 1 item).</summary>
public sealed class ListaOuUnicoConverter<T> : JsonConverter<List<T>>
{
    public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize<List<T>>(ref reader, options);
        }

        var unico = JsonSerializer.Deserialize<T>(ref reader, options);
        return unico is null ? [] : [unico];
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }

        writer.WriteEndArray();
    }
}

/// <summary>Lê e grava o backup e o log em C:\ProgramData\OtimizadorJogos.</summary>
public sealed class ArmazenamentoBackup
{
    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ArmazenamentoBackup(string pasta)
    {
        Pasta = pasta;
    }

    public static ArmazenamentoBackup Padrao() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorJogos"));

    public string Pasta { get; }

    public string ArquivoBackup => Path.Combine(Pasta, "backup.json");

    public string ArquivoLog => Path.Combine(Pasta, "log.txt");

    public bool Existe => File.Exists(ArquivoBackup);

    public Backup? Carregar()
    {
        if (!Existe)
        {
            return null;
        }

        // File.ReadAllText detecta o BOM que o PowerShell 5.1 grava
        var texto = File.ReadAllText(ArquivoBackup);
        var backup = JsonSerializer.Deserialize<Backup>(texto, Opcoes)
                     ?? throw new InvalidDataException("O arquivo de backup está vazio ou corrompido.");
        backup.Itens ??= [];
        return backup;
    }

    public void Salvar(Backup backup)
    {
        Directory.CreateDirectory(Pasta);

        // Grava num arquivo temporário e troca: o backup nunca fica pela metade
        var temporario = ArquivoBackup + ".tmp";
        File.WriteAllText(temporario, JsonSerializer.Serialize(backup, Opcoes), new System.Text.UTF8Encoding(false));
        File.Move(temporario, ArquivoBackup, overwrite: true);
    }

    /// <summary>Move o backup (já sem itens) para um arquivo histórico. Retorna o novo caminho.</summary>
    public string? Arquivar()
    {
        if (!Existe)
        {
            return null;
        }

        var destino = Path.Combine(Pasta, $"backup-revertido-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Move(ArquivoBackup, destino);
        return destino;
    }

    public void Log(string mensagem)
    {
        try
        {
            Directory.CreateDirectory(Pasta);
            File.AppendAllText(ArquivoLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [app] {mensagem}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // O log é só informativo; nunca deve impedir uma operação
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
