using System.ComponentModel.DataAnnotations;

namespace Catalogo.Features.Settings;

/// <summary>
/// Validação dos canais de contato (RN-67), na borda do serviço e não no atributo do
/// formulário.
///
/// O que motivou: o `maxlength` do campo é sugestão do navegador, e `Trim` não é validação.
/// Colar `+55 (88) 99654-1931` no campo de WhatsApp gravava sem reclamação, e a vitrine
/// passava a emitir um link que não abre conversa — no **botão principal** da página do
/// produto, sem nenhum sinal de erro no painel. O dono não tinha como descobrir sozinho
/// (R-07 de `REVIEW-T-31-2026-09-29`, que é o dono do R-03 de
/// `REVIEW-T-20-2026-09-29-round2.md`).
/// </summary>
public static class ContactValidation
{
    /// <summary>
    /// Mínimo de dígitos de um número com código de país. Abaixo disto, o que foi digitado
    /// não é um número discável de fora — quase sempre é o número local sem o DDI, que é o
    /// erro que a RN-67 existe para evitar.
    ///
    /// **Não se verifica o código do país em si**: isso exigiria lista de prefixos válidos,
    /// e recusar um país por ausência na lista seria pior que aceitar um número curto.
    /// </summary>
    public const int MinWhatsAppDigits = 12;

    /// <summary>Teto do formato E.164.</summary>
    public const int MaxWhatsAppDigits = 15;

    public static ContactOutcome Check(ContactDraft draft)
    {
        if (TooLong(draft.WhatsApp))
        {
            return Rejected(ContactField.WhatsApp);
        }

        // Texto preenchido que não tem dígito nenhum é **recusa**, não ausência: tratá-lo
        // como campo vazio apagava em silêncio o número que estava gravado.
        var typedSomething = !string.IsNullOrWhiteSpace(draft.WhatsApp);
        var whatsappDigits = DigitsOrNull(draft.WhatsApp);

        if (typedSomething && whatsappDigits is null)
        {
            return new ContactOutcome(
                ContactField.WhatsApp,
                "O WhatsApp precisa ser um número. Informe os dígitos com o código do país, "
                + "ou deixe o campo vazio para não oferecer este canal.");
        }

        if (whatsappDigits is { } whatsapp
            && (whatsapp.Length < MinWhatsAppDigits || whatsapp.Length > MaxWhatsAppDigits))
        {
            return new ContactOutcome(
                ContactField.WhatsApp,
                $"O WhatsApp precisa ter entre {MinWhatsAppDigits} e {MaxWhatsAppDigits} "
                + "dígitos, com o código do país e o DDD — por exemplo, 5588996541931. "
                + "Sem o código do país, o link de conversa não abre.");
        }

        if (TooLong(draft.Phone))
        {
            return Rejected(ContactField.Phone);
        }

        if (TooLong(draft.Email))
        {
            return Rejected(ContactField.Email);
        }

        if (Blank(draft.Email) is { } email && !new EmailAddressAttribute().IsValid(email))
        {
            return new ContactOutcome(
                ContactField.Email,
                "O e-mail não parece válido. Confira antes de salvar.");
        }

        return ContactOutcome.Saved();
    }

    /// <summary>
    /// Só os dígitos, ou nulo quando não sobrou nenhum. Nulo é ausência de canal, que é
    /// como a vitrine decide não exibir — a mesma convenção do resumo do produto (RN-04).
    /// </summary>
    public static string? DigitsOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string([.. value.Where(char.IsAsciiDigit)]);

        return digits.Length == 0 ? null : digits;
    }

    private static ContactOutcome Rejected(ContactField field) =>
        new(
            field,
            $"O texto passa de {PortalSettings.ContactMaxLength} caracteres.");

    /// <summary>
    /// O limite existe na coluna, e sem esta conferência ele aparecia como falha de banco em
    /// vez de mensagem — o `maxlength` do campo não vale para o que chega pelo circuito.
    /// </summary>
    private static bool TooLong(string? value) =>
        value?.Trim().Length > PortalSettings.ContactMaxLength;

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
