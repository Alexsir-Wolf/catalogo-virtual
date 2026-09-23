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
