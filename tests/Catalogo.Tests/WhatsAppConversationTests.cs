using Catalogo.Features.Storefront;

namespace Catalogo.Tests;

/// <summary>
/// A URL de conversa do WhatsApp (RN-55). São casos unitários porque o que pode quebrar
/// aqui é a montagem da URL, não o caminho até ela: nome com acento ou aspas é comum em
/// nome de produto e invalida o link se passar cru.
/// </summary>
public sealed class WhatsAppConversationTests
{
    private const string Number = "5588996541931";

    [Fact]
    public void A_conversa_sem_assunto_e_so_o_numero()
    {
        Assert.Equal($"https://wa.me/{Number}", WhatsAppConversation.UrlFor(Number));
    }

    /// <summary>
    /// O campo é digitado à mão e a URL de conversa aceita só dígitos — o que o dono
    /// escreveu com parênteses e espaço ainda precisa virar link válido.
    /// </summary>
    [Fact]
    public void Numero_digitado_com_pontuacao_vira_so_digitos()
    {
        Assert.Equal(
            $"https://wa.me/{Number}",
            WhatsAppConversation.UrlFor("+55 (88) 99654-1931"));
    }

    [Fact]
    public void RN_55_a_mensagem_pronta_cita_o_nome_do_produto()
    {
        Assert.Contains(
            "Notebook VAIO FE16",
            WhatsAppConversation.MessageFor("Notebook VAIO FE16"));
    }

    [Theory]
    [InlineData("Notebook VAIO FE16")]
    [InlineData("Cadeira Ergonômica Pró")]
    [InlineData("Papel A4 75g/m² \"Extra\"")]
    [InlineData("Cabo USB-C 1,8m & fonte 65W")]
    public void RN_55_o_nome_chega_intacto_depois_de_decodificar_a_url(string productName)
    {
        var url = WhatsAppConversation.UrlFor(Number, productName);
        var text = Uri.UnescapeDataString(url[(url.IndexOf("?text=") + 6)..]);

        Assert.StartsWith($"https://wa.me/{Number}?text=", url);
        Assert.Contains(productName, text);
    }

    /// <summary>
    /// A contraprova do caso acima: se o nome fosse concatenado cru, o espaço e o acento
    /// apareceriam na URL e o link chegaria partido ao aparelho do visitante.
    /// </summary>
    [Fact]
    public void O_nome_nao_aparece_cru_na_url()
    {
        var url = WhatsAppConversation.UrlFor(Number, "Cadeira Ergonômica Pró");

        Assert.DoesNotContain("Ergonômica", url);
        Assert.DoesNotContain(" ", url);
    }
}
