namespace Catalogo.Features.Account;

/// <summary>
/// Credencial semeada na primeira subida. Vem de configuração ou variável de ambiente —
/// nunca do código —, e a senha deve ser trocada no primeiro acesso (ADR-006).
/// </summary>
public class OwnerAccountOptions
{
    public const string SectionName = "Owner";

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(UserName) && !string.IsNullOrWhiteSpace(Password);
}
