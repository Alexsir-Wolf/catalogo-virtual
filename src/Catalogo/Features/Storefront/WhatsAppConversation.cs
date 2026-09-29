using System.Text.Encodings.Web;

namespace Catalogo.Features.Storefront;

/// <summary>
/// URL de conversa do WhatsApp a partir do número das Configurações (RN-67).
///
/// A mensagem pronta (RN-55) é o único texto do sistema que mistura conteúdo do catálogo
/// com copy fixa. Fica aqui, com **um único ponto de substituição**, porque é o que a
/// SPEC-UI pede para a UI-02 e porque espalhar a copy pelas telas é o caminho curto para
/// ela divergir.
/// </summary>
public static class WhatsAppConversation
{
    private const string MessageTemplate = "Olá! Tenho interesse no produto {0}.";

    /// <summary>Conversa sem assunto, como no rodapé da vitrine.</summary>
    public static string UrlFor(string number) => $"https://wa.me/{Digits(number)}";

    /// <summary>
    /// Conversa com a mensagem já escrita, citando o produto (RN-55). O nome vai para a
    /// URL, então é codificado — acento e aspas são comuns em nome de produto e quebram o
    /// link se passarem crus.
    /// </summary>
    public static string UrlFor(string number, string productName) =>
        $"{UrlFor(number)}?text={UrlEncoder.Default.Encode(MessageFor(productName))}";

    public static string MessageFor(string productName) =>
        string.Format(MessageTemplate, productName);

    /// <summary>
    /// A URL de conversa aceita só dígitos. O contrato da RN-67 já pede o número assim,
    /// mas o campo é digitado à mão — limpar aqui vale mais que confiar.
    /// </summary>
    private static string Digits(string number) => new([.. number.Where(char.IsAsciiDigit)]);
}
