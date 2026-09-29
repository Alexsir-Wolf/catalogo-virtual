using System.Text;
using Catalogo.Features.Settings;

namespace Catalogo.Tests;

/// <summary>
/// Varredura de profundidade de aninhamento. Existe porque o parser do PdfSharp desce
/// recursivamente por dicionários e arrays: 10 KB com 5.000 níveis esgotam a pilha e
/// **matam o processo**, com a árvore de páginas perfeitamente válida e sem repetir objeto
/// nenhum (R-01 de `REVIEW-T-31-2026-09-29`, segunda rodada).
///
/// Estes casos são unitários e rápidos de propósito: são a porta que impede o arquivo de
/// chegar ao parser.
/// </summary>
public sealed class PdfNestingScanTests
{
    [Fact]
    public void Documento_raso_tem_profundidade_pequena()
    {
        var depth = PdfNestingScan.MaxDepthOf(Ascii(
            "<</Type/Catalog/Pages 2 0 R>> <</Type/Page/MediaBox[0 0 595 842]>>"));

        Assert.Equal(2, depth);
    }

    [Fact]
    public void Array_aninhado_acima_do_teto_e_detectado()
    {
        var content = Ascii(Nested('[', ']', PdfNestingScan.MaxDepth + 10));

        Assert.True(PdfNestingScan.ExceedsMaxDepth(content));
    }

    [Fact]
    public void Dicionario_aninhado_acima_do_teto_e_detectado()
    {
        var content = Ascii(Nested("<<", ">>", PdfNestingScan.MaxDepth + 10));

        Assert.True(PdfNestingScan.ExceedsMaxDepth(content));
    }

    [Fact]
    public void Aninhamento_no_limite_ainda_passa()
    {
        var content = Ascii(Nested('[', ']', PdfNestingScan.MaxDepth));

        Assert.False(PdfNestingScan.ExceedsMaxDepth(content));
    }

    /// <summary>
    /// Delimitadores dentro de string literal são **texto**, não estrutura. Contá-los
    /// recusaria capa legítima cujo título tivesse um colchete.
    /// </summary>
    [Fact]
    public void Delimitadores_dentro_de_string_literal_nao_contam()
    {
        var content = Ascii($"/Titulo ({Nested('[', ']', 500)})");

        Assert.False(PdfNestingScan.ExceedsMaxDepth(content));
    }

    /// <summary>
    /// A barra invertida escapa o próximo byte: sem tratar isso, um `\)` terminaria a string
    /// cedo e o resto dela passaria a ser contado como estrutura.
    /// </summary>
    [Fact]
    public void Parentese_escapado_nao_encerra_a_string()
    {
        var content = Ascii($"/Titulo (fecha \\) e segue {Nested('[', ']', 500)})");

        Assert.False(PdfNestingScan.ExceedsMaxDepth(content));
    }

    [Fact]
    public void Delimitadores_dentro_de_comentario_nao_contam()
    {
        var content = Ascii($"% {Nested('[', ']', 500)}\n<</Type/Page>>");

        Assert.False(PdfNestingScan.ExceedsMaxDepth(content));
    }

    /// <summary>
    /// Dados de stream são binários: qualquer sequência pode aparecer ali, e contá-la
    /// produziria profundidade inventada.
    /// </summary>
    [Fact]
    public void Delimitadores_dentro_de_stream_nao_contam()
    {
        var content = Ascii($"<</Length 9>>stream\n{Nested('[', ']', 500)}\nendstream");

        Assert.False(PdfNestingScan.ExceedsMaxDepth(content));
    }

    [Fact]
    public void String_hexadecimal_nao_e_confundida_com_dicionario()
    {
        var content = Ascii("<</Id <48656C6C6F>>>");

        Assert.False(PdfNestingScan.ExceedsMaxDepth(content));
    }

    [Fact]
    public void Arquivo_vazio_nao_tem_profundidade()
    {
        Assert.Equal(0, PdfNestingScan.MaxDepthOf([]));
    }

    private static string Nested(char open, char close, int depth) =>
        new string(open, depth) + new string(close, depth);

    private static string Nested(string open, string close, int depth) =>
        string.Concat(Enumerable.Repeat(open, depth))
        + string.Concat(Enumerable.Repeat(close, depth));

    private static byte[] Ascii(string content) => Encoding.ASCII.GetBytes(content);
}
