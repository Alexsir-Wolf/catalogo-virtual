namespace Catalogo;

/// <summary>
/// O Supabase entrega a string de conexão no formato URI (postgresql://...),
/// que o Npgsql não interpreta. Esta conversão existe só por causa disso.
/// </summary>
public static class DatabaseConnectionString
{
    private const string UriScheme = "postgres";
    private const string AlternateUriScheme = "postgresql";

    public static string Normalize(string value)
    {
        if (!value.StartsWith($"{UriScheme}://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith($"{AlternateUriScheme}://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var credentials = uri.UserInfo.Split(':', 2);

        return string.Join(';',
            $"Host={uri.Host}",
            $"Port={(uri.Port > 0 ? uri.Port : 5432)}",
            $"Database={uri.AbsolutePath.Trim('/')}",
            $"Username={Uri.UnescapeDataString(credentials[0])}",
            $"Password={Uri.UnescapeDataString(credentials.Length > 1 ? credentials[1] : string.Empty)}",
            "SSL Mode=Require");
    }
}

/// <summary>
/// Descreve a forma da string de conexão — nunca os valores — para permitir
/// diagnosticar um erro de sintaxe sem expor credencial.
/// </summary>
public static class DatabaseConnectionStringShape
{
    private const string Redacted = "…";

    public static object Describe(string value)
    {
        if (value.Contains("://", StringComparison.Ordinal))
        {
            var scheme = value[..value.IndexOf("://", StringComparison.Ordinal)];
            return new { format = "uri", scheme, host = HostFromUri(value) };
        }

        var keys = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Contains('=') ? pair[..pair.IndexOf('=')].Trim() : $"{Redacted}{pair.Length}")
            .ToArray();

        return new { format = "keywords", keys };
    }

    private static string HostFromUri(string value)
    {
        try
        {
            return new Uri(value).Host;
        }
        catch (UriFormatException)
        {
            return Redacted;
        }
    }
}
