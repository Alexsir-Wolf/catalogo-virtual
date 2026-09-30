using System.Text;
using Catalogo.Features.Settings;
using PdfSharp;
using PdfSharp.Pdf;

namespace Catalogo.Tests;

/// <summary>
/// Validação do arquivo de capa (T-31, RN-63, RN-64). Não toca banco nem armazenamento: a
/// regra é sobre a estrutura do arquivo, e a ADR-017 manda recusar **no envio** em vez de
/// descobrir na geração.
/// </summary>
public sealed class CoverValidationTests
{
    [Fact]
    public void CA_34_pdf_de_uma_pagina_em_retrato_e_aceito()
    {
        var inspection = CoverValidation.Inspect(Pdf(PageSize.A4, pages: 1));

        Assert.True(inspection.Accepted);
    }

    /// <summary>
    /// CA-35: a RN-63 não manda apenas recusar — manda **informar quantas páginas foram
    /// encontradas**. Quem enviou o arquivo errado precisa saber o que enviou.
    /// </summary>
    [Fact]
    public void CA_35_pdf_com_tres_paginas_e_recusado_informando_a_contagem()
    {
        var inspection = CoverValidation.Inspect(Pdf(PageSize.A4, pages: 3));

        Assert.Equal(CoverRejection.PageCount, inspection.Rejection);
        Assert.Equal(3, inspection.PagesFound);
        Assert.Contains("3", CoverValidation.MessageFor(inspection));
    }

    /// <summary>
    /// A validação lê a estrutura, não a extensão nem o tipo declarado pelo navegador —
    /// PDF de origem desconhecida é entrada não confiável como qualquer upload (ADR-017).
    /// </summary>
    [Fact]
    public void Arquivo_que_nao_e_pdf_e_recusado()
    {
        var inspection = CoverValidation.Inspect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Assert.Equal(CoverRejection.NotAPdf, inspection.Rejection);
    }

    [Fact]
    public void Arquivo_vazio_e_recusado()
    {
        Assert.Equal(CoverRejection.NotAPdf, CoverValidation.Inspect([]).Rejection);
    }

    [Fact]
    public void RN_64_pdf_em_paisagem_e_recusado()
    {
        var inspection = CoverValidation.Inspect(Pdf(PageSize.A4, pages: 1, landscape: true));

        Assert.Equal(CoverRejection.Landscape, inspection.Rejection);
        Assert.Contains("retrato", CoverValidation.MessageFor(inspection));
    }

    /// <summary>
    /// Retrato não basta: a proporção precisa bater com a do miolo, senão o documento sai
    /// com faixa em branco ou corte ao lado das páginas de conteúdo (RN-64).
    /// </summary>
    [Fact]
    public void RN_64_retrato_com_proporcao_incompativel_e_recusado()
    {
        var inspection = CoverValidation.Inspect(Pdf(width: 500, height: 520));

        Assert.Equal(CoverRejection.AspectRatio, inspection.Rejection);
        Assert.Contains("proporção", CoverValidation.MessageFor(inspection));
    }

    /// <summary>
    /// Carta em retrato **é recusada**, e de propósito: a proporção dela é 0,774 contra
    /// 0,707 do A4 — 9,5% de diferença, que no papel é faixa branca ou corte visível ao
    /// lado do miolo. É exatamente o que a RN-64 existe para impedir, e a ADR-017 manda
    /// recusar no envio em vez de deixar aparecer na impressão.
    /// </summary>
    [Fact]
    public void RN_64_carta_em_retrato_e_recusada_por_proporcao()
    {
        var inspection = CoverValidation.Inspect(Pdf(PageSize.Letter, pages: 1));

        Assert.Equal(CoverRejection.AspectRatio, inspection.Rejection);
    }

    /// <summary>
    /// R-11 de `REVIEW-T-31-2026-09-29`: com tolerância **absoluta** de 0,06, esta página
    /// passava — 210 por 276 mm dá 0,761, que está a 0,053 de proporção do A4 e portanto
    /// dentro do limite antigo, mas a **7,6%** dele. No papel são cerca de dois centímetros
    /// de faixa branca ao lado do conteúdo.
    /// </summary>
    [Fact]
    public void RN_64_retrato_proximo_do_A4_mas_fora_da_tolerancia_relativa_e_recusado()
    {
        var inspection = CoverValidation.Inspect(Pdf(width: 595, height: 782));

        Assert.Equal(CoverRejection.AspectRatio, inspection.Rejection);
    }

    /// <summary>
    /// O outro lado da mesma régua: variação de milímetros no mesmo formato precisa passar,
    /// senão uma capa exportada com margem de sangria seria recusada sem motivo real.
    /// </summary>
    [Fact]
    public void RN_64_variacao_de_milimetros_no_mesmo_formato_e_aceita()
    {
        var inspection = CoverValidation.Inspect(Pdf(width: 592, height: 842));

        Assert.True(inspection.Accepted);
    }

    [Fact]
    public void Arquivo_acima_do_limite_e_recusado_antes_de_ser_lido()
    {
        var inspection = CoverValidation.Inspect(new byte[CoverValidation.MaxBytes + 1]);

        Assert.Equal(CoverRejection.TooLarge, inspection.Rejection);
    }

    [Fact]
    public void Capa_aceita_nao_tem_mensagem()
    {
        Assert.Empty(CoverValidation.MessageFor(CoverInspection.Ok()));
    }

    /// <summary>
    /// R-01 de `REVIEW-T-31-2026-09-29`: o nó `/Pages` se lista entre os próprios `/Kids`,
    /// e as APIs `PageCount`/`Pages` do PdfSharp achatam a árvore por recursão sem detectar
    /// o ciclo — 316 bytes esgotavam a pilha e **matavam o processo**, porque
    /// `StackOverflowException` não é capturável em .NET.
    ///
    /// Que este teste chegue a asserção alguma é metade do que ele prova: antes da
    /// correção, o executor de testes morria aqui em vez de reportar falha.
    /// </summary>
    [Fact]
    public void Arvore_de_paginas_ciclica_e_recusada_sem_derrubar_o_processo()
    {
        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[2 0 R 3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>"));

        Assert.Equal(CoverRejection.MalformedStructure, inspection.Rejection);
        Assert.Contains("estrutura", CoverValidation.MessageFor(inspection));
    }

    /// <summary>
    /// Ciclo mais longo que o auto-referente: dois nós intermediários apontando um para o
    /// outro. Detecção que só comparasse com o nó imediatamente anterior deixaria passar.
    /// </summary>
    [Fact]
    public void Ciclo_indireto_entre_nos_da_arvore_e_recusado()
    {
        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Pages/Kids[2 0 R]/Count 1>>"));

        Assert.Equal(CoverRejection.MalformedStructure, inspection.Rejection);
    }

    /// <summary>
    /// `MediaBox` é herdável no formato: produtores a declaram no nó `/Pages` e omitem na
    /// página. Ler só a folha faria uma capa legítima ser recusada por dimensão zero.
    /// </summary>
    [Fact]
    public void MediaBox_declarada_no_no_pai_e_herdada_pela_pagina()
    {
        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1/MediaBox[0 0 595 842]>>",
            "<</Type/Page/Parent 2 0 R>>"));

        Assert.True(inspection.Accepted);
    }

    /// <summary>
    /// RN-64: um A4 retrato com um quarto de volta **sai em paisagem** no papel. A
    /// orientação que importa é a que o leitor exibe, não a da caixa de mídia.
    /// </summary>
    [Fact]
    public void RN_64_retrato_girado_um_quarto_de_volta_e_recusado_como_paisagem()
    {
        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Rotate 90>>"));

        Assert.Equal(CoverRejection.Landscape, inspection.Rejection);
    }

    [Fact]
    public void Meia_volta_preserva_a_orientacao_e_e_aceita()
    {
        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Rotate 180>>"));

        Assert.True(inspection.Accepted);
    }

    /// <summary>
    /// `/Count` é declaração do produtor, não contagem. A recusa precisa vir do que a
    /// árvore realmente tem, senão um número inflado viraria alocação proporcional.
    /// </summary>
    [Fact]
    public void Count_declarado_e_ignorado_em_favor_da_arvore_real()
    {
        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R 4 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>"));

        Assert.Equal(CoverRejection.PageCount, inspection.Rejection);
        Assert.Equal(2, inspection.PagesFound);
    }

    [Fact]
    public void Documento_sem_no_de_paginas_e_recusado()
    {
        var inspection = CoverValidation.Inspect(RawPdf("<</Type/Catalog>>"));

        Assert.Equal(CoverRejection.MalformedStructure, inspection.Rejection);
    }

    /// <summary>
    /// R-01, segunda rodada: a árvore de páginas é **válida** e nada se repete — o que mata o
    /// processo é o aninhamento sintático numa chave qualquer, dentro do parser do PdfSharp,
    /// antes de qualquer travessia. Reproduzido com 10 KB e 5.000 níveis, saída `exit 127`.
    ///
    /// Que este caso chegue a uma asserção é o que ele prova: a detecção de ciclo e o teto de
    /// nós não veem nada aqui.
    /// </summary>
    [Fact]
    public void Aninhamento_sintatico_profundo_e_recusado_antes_de_chegar_ao_parser()
    {
        var nested = new string('[', 5_000) + new string(']', 5_000);

        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            $"<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Lixo {nested}>>"));

        Assert.Equal(CoverRejection.MalformedStructure, inspection.Rejection);
    }

    /// <summary>
    /// Os três contornos do guarda léxico, no caminho completo.
    ///
    /// O review de segundo round reproduziu cada um deles: bastava acrescentar lixo numa região que
    /// o parser nem visita para a varredura medir zero e o arquivo profundo chegar ao parser. Com
    /// `]` em excesso o contador ia a -6000 e comprava folga; com `stream` sem `endstream`, ou com
    /// **um** `(` sem fechamento, a varredura pulava até o fim do arquivo.
    ///
    /// **Estes casos ou devolvem a recusa ou derrubam o processo de teste** — `StackOverflowException`
    /// não é capturável, então não existe o meio termo de "falhou a asserção". É o formato mais
    /// honesto de verificação disponível para este defeito.
    /// </summary>
    [Theory]
    [InlineData("]]]]]]]]]]")]
    [InlineData("stream")]
    [InlineData("(")]
    public void Lixo_que_desligava_a_varredura_nao_deixa_o_arquivo_profundo_chegar_ao_parser(string lixo)
    {
        var nested = new string('[', 5_000) + new string(']', 5_000);

        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            $"<</Type/Pages/Kids[3 0 R]/Count 1>> {lixo}",
            $"<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Lixo {nested}>>"));

        Assert.Equal(CoverRejection.MalformedStructure, inspection.Rejection);
    }

    /// <summary>
    /// O teto de nós contava só os que têm identidade de objeto, e era cego para dicionários
    /// **diretos** — uma árvore de centenas de milhares de páginas diretas cabia no limite de
    /// tamanho, nunca tocava o teto, e a fila sozinha custava centenas de megabytes.
    /// </summary>
    [Fact]
    public void Arvore_larga_de_paginas_diretas_para_no_teto_em_vez_de_crescer()
    {
        var kids = string.Concat(
            Enumerable.Repeat("<</Type/Page/MediaBox[0 0 595 842]>>", 6_000));

        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            $"<</Type/Pages/Count 1/Kids[{kids}]>>"));

        // Recusado por estrutura ou por contagem — o que importa é **terminar barato**, sem
        // percorrer a árvore inteira nem empilhar tudo.
        Assert.False(inspection.Accepted);
    }

    /// <summary>
    /// Acima do teto, a mensagem diz **"mais de"** em vez de inventar um número.
    ///
    /// A contagem para no teto, e o valor em que parou ia para a mensagem como se fosse o total: um
    /// PDF de 300 páginas produzia "este arquivo tem 65". O cenário é o erro mais provável desta
    /// tela — o dono envia o catálogo inteiro no lugar da capa — e a RN-63 pede justamente que a
    /// recusa informe **quantas** foram encontradas. Um número inventado é pior que nenhum.
    /// </summary>
    [Fact]
    public void RN_63_acima_do_teto_a_mensagem_nao_inventa_a_contagem()
    {
        var kids = string.Concat(
            Enumerable.Repeat("<</Type/Page/MediaBox[0 0 595 842]>>", 300));

        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            $"<</Type/Pages/Count 1/Kids[{kids}]>>"));

        Assert.Equal(CoverRejection.PageCount, inspection.Rejection);
        Assert.True(inspection.PageCountTruncated);

        var message = CoverValidation.MessageFor(inspection);

        Assert.Contains($"mais de {CoverValidation.MaxPagesToCount}", message);
    }

    /// <summary>
    /// Abaixo do teto a contagem é exata: é isso que a RN-63 pede, e o "mais de" existe só para o
    /// caso em que o número seria inventado.
    /// </summary>
    [Fact]
    public void RN_63_abaixo_do_teto_a_contagem_e_exata()
    {
        var kids = string.Concat(
            Enumerable.Repeat("<</Type/Page/MediaBox[0 0 595 842]>>", 3));

        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            $"<</Type/Pages/Count 3/Kids[{kids}]>>"));

        Assert.Equal(CoverRejection.PageCount, inspection.Rejection);
        Assert.False(inspection.PageCountTruncated);
        Assert.Equal(3, inspection.PagesFound);
        Assert.Contains("tem 3", CoverValidation.MessageFor(inspection));
    }

    /// <summary>
    /// `MediaBox` com os cantos invertidos **só no eixo X** era recusada com "o arquivo não é um PDF
    /// que possamos ler".
    ///
    /// O formato permite os cantos em qualquer ordem, e a leitura da dimensão já usava valor
    /// absoluto — mas a seleção da caixa exigia largura positiva, então a caixa era descartada como
    /// ausente e a página caía no ramo de dimensão inválida. Com os **dois** eixos invertidos
    /// funcionava, o que deixava o defeito parecendo tratado.
    /// </summary>
    [Fact]
    public void MediaBox_com_cantos_invertidos_no_eixo_x_e_aceita()
    {
        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[595 0 0 842]>>"));

        Assert.True(inspection.Accepted);
    }

    /// <summary>
    /// `/Rotate` é definido em múltiplos de 90. Fora disso, a divisão inteira tratava o valor
    /// como zero: `/Rotate 45` passava como retrato.
    /// </summary>
    [Fact]
    public void Rotacao_fora_de_multiplo_de_noventa_e_recusada()
    {
        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Rotate 45>>"));

        Assert.Equal(CoverRejection.MalformedStructure, inspection.Rejection);
    }

    /// <summary>
    /// Proporção certa não basta: uma página de dezenas de quilômetros é proporcional ao A4 e
    /// era **aceita**, seguindo para a concatenação de T-32.
    /// </summary>
    [Fact]
    public void Pagina_acima_do_limite_do_formato_e_recusada_apesar_da_proporcao()
    {
        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 210000 297000]>>"));

        Assert.Equal(CoverRejection.MalformedStructure, inspection.Rejection);
    }

    /// <summary>
    /// O formato permite os cantos da caixa em qualquer ordem. Lida como dimensão negativa, a
    /// capa era recusada com "não é um PDF" — mensagem que manda o dono procurar um problema
    /// que não existe.
    /// </summary>
    [Fact]
    public void MediaBox_com_cantos_invertidos_e_aceita()
    {
        var inspection = CoverValidation.Inspect(RawPdf(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 842 595 0]>>"));

        Assert.True(inspection.Accepted);
    }

    /// <summary>
    /// PDF montado byte a byte, com tabela xref calculada. O `PdfDocument` do PdfSharp não
    /// serve aqui: ele não deixa produzir árvore inconsistente, que é justamente o que
    /// estes casos precisam enviar.
    /// </summary>
    private static byte[] RawPdf(params string[] objects)
    {
        using var buffer = new MemoryStream();

        void Write(string text) => buffer.Write(Encoding.ASCII.GetBytes(text));

        Write("%PDF-1.4\n");

        var offsets = new List<long>();

        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(buffer.Length);
            Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var startxref = buffer.Length;

        Write($"xref\n0 {objects.Length + 1}\n");
        Write("0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            Write($"{offset:D10} 00000 n \n");
        }

        Write($"trailer\n<</Size {objects.Length + 1}/Root 1 0 R>>\n");
        Write($"startxref\n{startxref}\n%%EOF\n");

        return buffer.ToArray();
    }

    private static byte[] Pdf(PageSize size, int pages, bool landscape = false)
    {
        using var document = new PdfDocument();

        for (var page = 0; page < pages; page++)
        {
            var added = document.AddPage();
            added.Size = size;

            if (landscape)
            {
                added.Orientation = PageOrientation.Landscape;
            }
        }

        return Bytes(document);
    }

    private static byte[] Pdf(double width, double height)
    {
        using var document = new PdfDocument();

        var page = document.AddPage();
        page.Width = XUnitHelper.Points(width);
        page.Height = XUnitHelper.Points(height);

        return Bytes(document);
    }

    private static byte[] Bytes(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);

        return stream.ToArray();
    }

    private static class XUnitHelper
    {
        /// <summary>
        /// O tipo de medida do PdfSharp se chama <c>XUnit</c>, que colide com o xUnit em
        /// arquivo de teste. Envolver a conversão aqui evita apelidar o pacote inteiro.
        /// </summary>
        public static PdfSharp.Drawing.XUnit Points(double value) =>
            PdfSharp.Drawing.XUnit.FromPoint(value);
    }
}
