using Catalogo.Features.Settings;

namespace Catalogo.Tests;

/// <summary>
/// Validação dos canais de contato (RN-67). São casos unitários porque o que pode quebrar é
/// a regra de formato, não o caminho até o banco.
///
/// R-07 de `REVIEW-T-31-2026-09-29`: antes, `Trim` era toda a validação. O número colado no
/// formato local ia para o banco e a vitrine emitia um link de conversa que não abre — no
/// botão principal da página do produto, sem sinal de erro no painel.
/// </summary>
public sealed class ContactValidationTests
{
    /// <summary>
    /// O caso que originou o finding: o formato que o dono naturalmente copia da agenda, e
    /// que o próprio placeholder do campo de telefone sugere.
    /// </summary>
    [Fact]
    public void RN_67_numero_local_sem_codigo_do_pais_e_recusado()
    {
        var outcome = ContactValidation.Check(new ContactDraft { WhatsApp = "(88) 99654-1931" });

        Assert.Equal(ContactField.WhatsApp, outcome.Rejected);
        Assert.Contains("código do país", outcome.Message!);
    }

    [Theory]
    [InlineData("5588996541931")]
    [InlineData("+55 (88) 99654-1931")]
    [InlineData("55 88 99654 1931")]
    public void RN_67_numero_com_codigo_do_pais_e_aceito_com_ou_sem_pontuacao(string typed)
    {
        var outcome = ContactValidation.Check(new ContactDraft { WhatsApp = typed });

        Assert.True(outcome.Succeeded);
    }

    /// <summary>
    /// A RN-67 pede "apenas dígitos": o que a tela aceita com pontuação precisa chegar ao
    /// banco sem ela, senão a URL de conversa sai quebrada mesmo com número certo.
    /// </summary>
    [Fact]
    public void RN_67_a_pontuacao_e_removida_antes_de_gravar()
    {
        Assert.Equal("5588996541931", ContactValidation.DigitsOrNull("+55 (88) 99654-1931"));
    }

    [Fact]
    public void Campo_sem_nenhum_digito_e_ausencia_e_nao_erro()
    {
        var outcome = ContactValidation.Check(new ContactDraft { WhatsApp = "   " });

        Assert.True(outcome.Succeeded);
        Assert.Null(ContactValidation.DigitsOrNull("   "));
    }

    [Fact]
    public void Numero_longo_demais_para_o_formato_internacional_e_recusado()
    {
        var outcome = ContactValidation.Check(
            new ContactDraft { WhatsApp = new string('5', ContactValidation.MaxWhatsAppDigits + 1) });

        Assert.Equal(ContactField.WhatsApp, outcome.Rejected);
    }

    [Fact]
    public void Email_sem_formato_valido_e_recusado()
    {
        var outcome = ContactValidation.Check(new ContactDraft { Email = "vendas arroba exemplo" });

        Assert.Equal(ContactField.Email, outcome.Rejected);
    }

    [Fact]
    public void Email_valido_e_aceito()
    {
        var outcome = ContactValidation.Check(
            new ContactDraft { Email = "vendas@exemplo.com.br" });

        Assert.True(outcome.Succeeded);
    }

    /// <summary>
    /// O limite da coluna conferido aqui, e não no banco: sem isso o texto longo chegava
    /// como falha de gravação em vez de mensagem no campo.
    /// </summary>
    [Fact]
    public void Texto_acima_do_limite_da_coluna_e_recusado_com_mensagem()
    {
        var outcome = ContactValidation.Check(new ContactDraft
        {
            Phone = new string('9', PortalSettings.ContactMaxLength + 1)
        });

        Assert.Equal(ContactField.Phone, outcome.Rejected);
        Assert.Contains(PortalSettings.ContactMaxLength.ToString(), outcome.Message!);
    }

    [Fact]
    public void Contato_inteiro_vazio_e_aceito()
    {
        Assert.True(ContactValidation.Check(new ContactDraft()).Succeeded);
    }
}
