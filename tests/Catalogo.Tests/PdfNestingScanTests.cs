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
    public void Documento_raso_e_seguro()
    {
        var verdict = PdfNestingScan.Scan(Ascii(
            "<</Type/Catalog/Pages 2 0 R>> <</Type/Page/MediaBox[0 0 595 842]>>"));

        Assert.Equal(PdfScanVerdict.Safe, verdict);
    }

    /// <summary>
    /// **O contorno de um byte.** Um `(` sem fechamento fazia a varredura pular até o fim do
    /// arquivo e medir zero: os 5.000 níveis reais que vinham depois não eram vistos, o arquivo
    /// chegava ao parser e o processo morria por estouro de pilha — que não é capturável.
    ///
    /// O que se afirma aqui é que o guarda **recusa o que não consegue medir**, em vez de aceitar
    /// por omissão. Um PDF bem formado não tem string sem fim, então a recusa não custa capa
    /// legítima nenhuma.
    /// </summary>
    [Fact]
    public void String_literal_sem_fechamento_e_recusada()
    {
        var content = Ascii($"/Titulo (sem fim {Nested('[', ']', PdfNestingScan.MaxDepth + 10)}");

        Assert.Equal(PdfScanVerdict.UnterminatedToken, PdfNestingScan.Scan(content));
        Assert.True(PdfNestingScan.IsUnsafeToParse(content));
    }

    /// <summary>
    /// A palavra `stream` sem `endstream` desligava a varredura pelo mesmo mecanismo, com seis
    /// bytes em vez de um.
    /// </summary>
    [Fact]
    public void Stream_sem_endstream_e_recusado()
    {
        var content = Ascii($"<</Length 9>>stream\n{Nested('[', ']', PdfNestingScan.MaxDepth + 10)}");

        Assert.Equal(PdfScanVerdict.UnterminatedToken, PdfNestingScan.Scan(content));
    }

    /// <summary>
    /// **O quarto contorno, e o de vinte bytes.** `stream` era reconhecido em qualquer posição,
    /// inclusive dentro de um **nome** — `/Xstream` bastava para a varredura saltar até o próximo
    /// `endstream` e engolir o aninhamento que estava no meio, devolvendo `Safe`. O parser do
    /// PdfSharp não tem essa regra: ele lê o array e recursa até o processo morrer
    /// (R-01 de `REVIEW-T-31-2026-09-30-round2`).
    ///
    /// A palavra só abre stream quando é **token**: precedida de delimitador ou espaço, nunca de
    /// `/`, e depois do `>>` que fecha o dicionário. Este caso usa a forma exata do payload que
    /// matou o processo na sondagem, com `/Xstream` antes do aninhamento e `endstream` depois.
    /// </summary>
    [Fact]
    public void Nome_terminado_em_stream_nao_desliga_a_varredura()
    {
        var content = Ascii(
            "<</Type/Page/Xstream 0/Lixo "
            + Nested('[', ']', PdfNestingScan.MaxDepth + 10)
            + " endstream>>");

        Assert.Equal(PdfScanVerdict.TooDeep, PdfNestingScan.Scan(content));
        Assert.True(PdfNestingScan.IsUnsafeToParse(content));
    }

    /// <summary>
    /// A contraprova do caso acima: stream de verdade continua sendo saltado, senão a correção
    /// trocaria o contorno por recusa de capa legítima — todo PDF com imagem tem stream binário,
    /// e bytes comprimidos contêm `[` e `<<` em qualquer quantidade.
    /// </summary>
    [Fact]
    public void Stream_de_verdade_continua_sendo_saltado()
    {
        var content = Ascii(
            "<</Length 40>>stream\n"
            + Nested('[', ']', PdfNestingScan.MaxDepth + 10)
            + "\nendstream <</Type/Page/MediaBox[0 0 595 842]>>");

        Assert.Equal(PdfScanVerdict.Safe, PdfNestingScan.Scan(content));
    }

    [Fact]
    public void String_hexadecimal_sem_fechamento_e_recusada()
    {
        var content = Ascii($"<</Id <48656C6C6F {Nested('[', ']', PdfNestingScan.MaxDepth + 10)}");

        Assert.Equal(PdfScanVerdict.UnterminatedToken, PdfNestingScan.Scan(content));
    }

    /// <summary>
    /// O terceiro contorno, e o mais barato de todos: fechamento em excesso numa região que o
    /// parser nem visita. Sem piso no contador, seis mil `]` levavam a profundidade a -6000 e
    /// **compravam folga** para o aninhamento real que vinha depois.
    /// </summary>
    [Fact]
    public void Fechamento_em_excesso_e_recusado_em_vez_de_comprar_folga()
    {
        var content = Ascii(
            new string(']', 6000) + Nested('[', ']', PdfNestingScan.MaxDepth + 10));

        Assert.Equal(PdfScanVerdict.UnbalancedDelimiters, PdfNestingScan.Scan(content));
        Assert.True(PdfNestingScan.IsUnsafeToParse(content));
    }

    [Fact]
    public void Fechamento_de_dicionario_em_excesso_e_recusado()
    {
        Assert.Equal(
            PdfScanVerdict.UnbalancedDelimiters,
            PdfNestingScan.Scan(Ascii("<</Type/Page>> >>")));
    }

    [Fact]
    public void Array_aninhado_acima_do_teto_e_detectado()
    {
        var content = Ascii(Nested('[', ']', PdfNestingScan.MaxDepth + 10));

        Assert.True(PdfNestingScan.IsUnsafeToParse(content));
    }

    [Fact]
    public void Dicionario_aninhado_acima_do_teto_e_detectado()
    {
        var content = Ascii(Nested("<<", ">>", PdfNestingScan.MaxDepth + 10));

        Assert.True(PdfNestingScan.IsUnsafeToParse(content));
    }

    [Fact]
    public void Aninhamento_no_limite_ainda_passa()
    {
        var content = Ascii(Nested('[', ']', PdfNestingScan.MaxDepth));

        Assert.False(PdfNestingScan.IsUnsafeToParse(content));
    }

    /// <summary>
    /// Delimitadores dentro de string literal são **texto**, não estrutura. Contá-los
    /// recusaria capa legítima cujo título tivesse um colchete.
    /// </summary>
    [Fact]
    public void Delimitadores_dentro_de_string_literal_nao_contam()
    {
        var content = Ascii($"/Titulo ({Nested('[', ']', 500)})");

        Assert.False(PdfNestingScan.IsUnsafeToParse(content));
    }

    /// <summary>
    /// A barra invertida escapa o próximo byte: sem tratar isso, um `\)` terminaria a string
    /// cedo e o resto dela passaria a ser contado como estrutura.
    /// </summary>
    [Fact]
    public void Parentese_escapado_nao_encerra_a_string()
    {
        var content = Ascii($"/Titulo (fecha \\) e segue {Nested('[', ']', 500)})");

        Assert.False(PdfNestingScan.IsUnsafeToParse(content));
    }

    [Fact]
    public void Delimitadores_dentro_de_comentario_nao_contam()
    {
        var content = Ascii($"% {Nested('[', ']', 500)}\n<</Type/Page>>");

        Assert.False(PdfNestingScan.IsUnsafeToParse(content));
    }

    /// <summary>
    /// Dados de stream são binários: qualquer sequência pode aparecer ali, e contá-la
    /// produziria profundidade inventada.
    /// </summary>
    [Fact]
    public void Delimitadores_dentro_de_stream_nao_contam()
    {
        var content = Ascii($"<</Length 9>>stream\n{Nested('[', ']', 500)}\nendstream");

        Assert.False(PdfNestingScan.IsUnsafeToParse(content));
    }

    [Fact]
    public void String_hexadecimal_nao_e_confundida_com_dicionario()
    {
        var content = Ascii("<</Id <48656C6C6F>>>");

        Assert.False(PdfNestingScan.IsUnsafeToParse(content));
    }

    [Fact]
    public void Arquivo_vazio_e_seguro()
    {
        Assert.Equal(PdfScanVerdict.Safe, PdfNestingScan.Scan([]));
    }

    /// <summary>
    /// Acima do teto o veredito diz **qual** motivo, e não só que é inseguro: os três motivos
    /// levam à mesma recusa na tela, mas confundi-los na investigação custaria uma hora.
    /// </summary>
    [Fact]
    public void Aninhamento_acima_do_teto_e_reportado_como_profundo()
    {
        Assert.Equal(
            PdfScanVerdict.TooDeep,
            PdfNestingScan.Scan(Ascii(Nested('[', ']', PdfNestingScan.MaxDepth + 10))));
    }

    private static string Nested(char open, char close, int depth) =>
        new string(open, depth) + new string(close, depth);

    private static string Nested(string open, string close, int depth) =>
        string.Concat(Enumerable.Repeat(open, depth))
        + string.Concat(Enumerable.Repeat(close, depth));

    private static byte[] Ascii(string content) => Encoding.ASCII.GetBytes(content);
}
