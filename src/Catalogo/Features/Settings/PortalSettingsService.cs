using Catalogo.Data;
using Catalogo.Features.Account;
using Catalogo.Features.Media;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Catalogo.Features.Settings;

/// <summary>Dados de contato como a tela os entrega e a vitrine os consome.</summary>
public sealed record ContactDraft
{
    public string? WhatsApp { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }
}

/// <summary>Qual canal foi recusado, para o erro aparecer no campo certo.</summary>
public enum ContactField
{
    WhatsApp,
    Phone,
    Email
}

/// <summary>
/// Resultado da gravação do contato. Recusa **não grava nada** — um canal inválido não
/// pode derrubar os outros dois que já estavam certos.
/// </summary>
public sealed record ContactOutcome(ContactField? Rejected = null, string? Message = null)
{
    public bool Succeeded => Rejected is null;

    public static ContactOutcome Saved() => new();
}

public enum PasswordFailure
{
    None,
    CurrentPasswordWrong,
    NewPasswordRejected
}

public sealed record PasswordOutcome(PasswordFailure Failure, string? Message = null)
{
    public bool Succeeded => Failure == PasswordFailure.None;

    public static PasswordOutcome Changed() => new(PasswordFailure.None);
}

/// <summary>
/// Configurações do portal (RN-61 a RN-68). Reúne o que o dono pode mudar sem depender de
/// versão nova do sistema: a capa do PDF, os dados de contato e a própria senha.
///
/// **A capa vai para o armazenamento de objeto**, não para o disco do container. O texto
/// de T-31 e a seção 9 da arquitetura dizem "volume de disco", mas são anteriores à
/// revisão 0.7: a ADR-018 põe no Supabase "as imagens **e da capa**", e o disco do Render
/// é efêmero — capa em disco desapareceria a cada publicação.
/// </summary>
public sealed class PortalSettingsService(
    IDbContextFactory<CatalogDbContext> contextFactory,
    IObjectStorage storage,
    IOptions<ObjectStorageOptions> storageOptions,
    UserManager<OwnerAccount> users,
    ILogger<PortalSettingsService> logger)
{
    private readonly ObjectStorageOptions options = storageOptions.Value;

    /// <summary>
    /// O registro único. Nascer vazio é o estado real de um portal recém-publicado, e é o
    /// que a UI-10 desenha como `semCapa`.
    ///
    /// **Leitura é só leitura.** Antes, a ausência da linha fazia este método inseri-la — e
    /// como o layout da vitrine chama aqui em toda requisição, uma página **pública e
    /// anônima** abria transação de escrita, e dois visitantes simultâneos num banco recém
    /// implantado disputavam a mesma chave fixa: um recebia `23505` e a vitrine devolvia 500
    /// (R-04 de `REVIEW-T-31-2026-09-29`). A linha passou a ser semeada na migration, e a
    /// ausência — banco anterior ao seed — devolve uma instância vazia em memória em vez de
    /// gravar. Também é o que mantém a vitrine barata para o cache de T-21.
    /// </summary>
    public async Task<PortalSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var settings = await context.PortalSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == PortalSettings.SingletonId, cancellationToken);

        return settings ?? new PortalSettings();
    }

    /// <summary>
    /// Grava os canais de contato. Campo em branco é ausência, não string vazia — a
    /// vitrine decide o que exibir pela ausência, como faz com o resumo do produto (RN-04).
    ///
    /// A vitrine lê do banco a cada requisição, então a alteração reflete imediatamente
    /// (RN-68, CA-38). Quando o cache de T-21 entrar, é aqui que a invalidação precisa
    /// ser disparada — escrita como qualquer outra.
    /// </summary>
    public async Task<ContactOutcome> SaveContactAsync(
        ContactDraft draft,
        CancellationToken cancellationToken = default)
    {
        if (ContactValidation.Check(draft) is { Succeeded: false } rejection)
        {
            return rejection;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await TrackedAsync(context, cancellationToken);

        // O WhatsApp é gravado **só com dígitos**, que é o que a RN-67 exige e o que a URL
        // de conversa aceita. O telefone é gravado como digitado: é texto para o visitante
        // ler, e quem precisa de dígitos ali é o `href`, que os extrai na hora.
        settings.WhatsApp = ContactValidation.DigitsOrNull(draft.WhatsApp);
        settings.Phone = Blank(draft.Phone);
        settings.Email = Blank(draft.Email);

        await context.SaveChangesAsync(cancellationToken);

        return ContactOutcome.Saved();
    }

    /// <summary>
    /// Valida e grava a capa. **Recusa preserva a anterior**: a validação acontece antes
    /// de qualquer escrita, então um arquivo inválido não chega ao armazenamento nem toca
    /// o registro (RN-62, ADR-017).
    ///
    /// O nome é novo a cada envio, como nas derivadas de foto (RN-13): substituir no mesmo
    /// nome deixaria cache de borda servindo a capa antiga.
    /// </summary>
    public async Task<CoverInspection> SaveCoverAsync(
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        var inspection = CoverValidation.Inspect(content);
        if (!inspection.Accepted)
        {
            logger.LogInformation(
                "Capa recusada: {Motivo}. Páginas encontradas: {Paginas}.",
                inspection.Rejection,
                inspection.PagesFound);

            return inspection;
        }

        var objectName = $"capa/{Guid.NewGuid():N}.pdf";
        await storage.UploadAsync(
            options.PublicBucket,
            objectName,
            content,
            "application/pdf",
            cancellationToken);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await TrackedAsync(context, cancellationToken);

        var replaced = settings.CoverFileName;
        settings.CoverFileName = objectName;

        await context.SaveChangesAsync(cancellationToken);

        if (replaced is not null)
        {
            // Mesma decisão das derivadas substituídas em T-13: a antiga não é apagada na
            // hora, porque um PDF gerado ou uma página em cache ainda podem apontar para
            // ela. O nome vai para o log para não ficar sem rastro.
            logger.LogInformation("Capa substituída. Objeto sem referência: {Nome}.", replaced);
        }

        return inspection;
    }

    public string CoverUrlFor(string objectName) => storage.PublicUrlFor(objectName);

    /// <summary>
    /// Troca da senha do dono (RN-66). É o **único** caminho previsto de troca no sistema
    /// (RN-59), e exige a senha atual: sem isso, uma sessão esquecida aberta viraria troca
    /// de dono. Senha atual errada não altera nada.
    /// </summary>
    public async Task<PasswordOutcome> ChangePasswordAsync(
        string userName,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var owner = await users.FindByNameAsync(userName);
        if (owner is null)
        {
            return new PasswordOutcome(
                PasswordFailure.CurrentPasswordWrong,
                "Não foi possível confirmar a senha atual.");
        }

        var result = await users.ChangePasswordAsync(owner, currentPassword, newPassword);
        if (result.Succeeded)
        {
            // A ADR-006 prevê troca obrigatória no primeiro acesso; trocar por vontade
            // própria também cumpre a exigência e a desliga.
            owner.MustChangePassword = false;
            await users.UpdateAsync(owner);

            return PasswordOutcome.Changed();
        }

        // O Identity devolve `PasswordMismatch` para senha atual errada e códigos de
        // política para a nova. Separar os dois é o que permite pôr o erro no campo certo
        // (UI-10.senhaIncorreta).
        var mismatch = result.Errors.Any(error => error.Code == "PasswordMismatch");

        return new PasswordOutcome(
            mismatch ? PasswordFailure.CurrentPasswordWrong : PasswordFailure.NewPasswordRejected,
            mismatch
                ? "A senha atual não confere. Nada foi alterado."
                : string.Join(" ", result.Errors.Select(error => error.Description)));
    }

    private static async Task<PortalSettings> TrackedAsync(
        CatalogDbContext context,
        CancellationToken cancellationToken)
    {
        var settings = await context.PortalSettings
            .SingleOrDefaultAsync(entity => entity.Id == PortalSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            settings = new PortalSettings();
            context.PortalSettings.Add(settings);
        }

        return settings;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
