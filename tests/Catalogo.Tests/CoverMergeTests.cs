using Catalogo.Features.PdfExport;
using PdfSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Catalogo.Tests;

/// <summary>
/// Concatenação da capa com as páginas compostas (T-32, RN-36, RN-37, ADR-017).
///
/// A capa é arquivo do dono e entra **sem nenhuma alteração** — o sistema não escreve nada
/// sobre ela. É a regra mais simples desta tarefa e a mais fácil de violar sem perceber:
/// bastaria a biblioteca acrescentar numeração ou carimbar metadados.
///
/// As páginas destes casos são identificadas por **geometria**, não por texto: o PdfSharp exige
/// um resolvedor de fontes para desenhar string, e um retângulo em coordenada própria distingue
/// páginas e prova ordem com menos aparato.
/// </summary>
public sealed class CoverMergeTests
{
    /// <summary>
    /// CA-37: a capa é a **primeira** página, o conteúdo vem em seguida, e a contagem total é a
    /// soma das duas partes.
    /// </summary>
    [Fact]
    public void CA_37_a_capa_e_a_primeira_pagina_e_a_contagem_total_confere()
    {
        var cover = Pdf(marks: [0]);
        var content = Pdf(marks: [10, 20, 30]);

        var merged = CoverMerge.Merge(cover, content);

        using var document = Open(merged);
        using var original = Open(cover);

        Assert.Equal(4, document.PageCount);
        Assert.Equal(ContentOf(original.Pages[0]), ContentOf(document.Pages[0]));
    }

    /// <summary>
    /// RN-37: nada é escrito sobre a capa. O fluxo de conteúdo e as dimensões da primeira página
    /// do documento final são **idênticos** aos do arquivo enviado — se a concatenação
    /// acrescentasse numeração ou carimbo, deixariam de ser.
    /// </summary>
    [Fact]
    public void RN_37_a_capa_sai_sem_nada_escrito_sobre_ela()
    {
        var cover = Pdf(marks: [7]);

        var merged = CoverMerge.Merge(cover, Pdf(marks: [50, 60]));

        using var original = Open(cover);
        using var result = Open(merged);

        Assert.Equal(ContentOf(original.Pages[0]), ContentOf(result.Pages[0]));
        Assert.Equal(original.Pages[0].Width.Point, result.Pages[0].Width.Point);
        Assert.Equal(original.Pages[0].Height.Point, result.Pages[0].Height.Point);
    }

    /// <summary>
    /// A ordem do miolo é preservada: a página 1 do conteúdo vira a página 2 do documento, e
    /// assim por diante. Inverter aqui produziria um catálogo com as categorias fora de ordem.
    /// </summary>
    [Fact]
    public void A_ordem_das_paginas_de_conteudo_e_preservada()
    {
        int[] marks = [11, 22, 33, 44];
        var content = Pdf(marks);

        var merged = CoverMerge.Merge(Pdf(marks: [0]), content);

        using var original = Open(content);
        using var document = Open(merged);

        for (var index = 0; index < marks.Length; index++)
        {
            Assert.Equal(ContentOf(original.Pages[index]), ContentOf(document.Pages[index + 1]));
        }
    }

    /// <summary>
    /// Capa de mais de uma página não é aceita pela validação de T-31, mas a concatenação não
    /// depende disso: ela une o que recebe. Se a regra mudar, este caminho já funciona.
    /// </summary>
    [Fact]
    public void Capa_de_duas_paginas_entra_inteira_na_frente()
    {
        var cover = Pdf(marks: [1, 2]);

        var merged = CoverMerge.Merge(cover, Pdf(marks: [99]));

        using var original = Open(cover);
        using var document = Open(merged);

        Assert.Equal(3, document.PageCount);
        Assert.Equal(ContentOf(original.Pages[0]), ContentOf(document.Pages[0]));
        Assert.Equal(ContentOf(original.Pages[1]), ContentOf(document.Pages[1]));
    }

    /// <summary>
    /// O documento final não herda metadados de nenhuma das partes: carregar o produtor do
    /// editor que fez a capa confundiria quem for inspecionar o arquivo.
    /// </summary>
    [Fact]
    public void O_documento_final_nao_herda_metadados_da_capa()
    {
        var cover = PdfWithTitle("Capa feita no editor do cliente");

        var merged = CoverMerge.Merge(cover, Pdf(marks: [5]));

        using var document = Open(merged);

        Assert.DoesNotContain("editor do cliente", document.Info.Title ?? string.Empty);
    }

    /// <summary>
    /// O resultado é um PDF que reabre: a concatenação produz documento novo com páginas
    /// importadas, e não uma costura dos bytes dos dois originais — que geraria arquivo aceito
    /// só por alguns leitores.
    /// </summary>
    [Fact]
    public void O_resultado_e_um_pdf_valido_que_reabre()
    {
        var merged = CoverMerge.Merge(Pdf(marks: [0]), Pdf(marks: [10, 20]));

        Assert.Equal("%PDF"u8.ToArray(), merged.Take(4).ToArray());

        using var document = Open(merged);
        Assert.Equal(3, document.PageCount);
    }

    private static PdfDocument Open(byte[] pdf)
    {
        var stream = new MemoryStream(pdf, writable: false);

        return PdfReader.Open(stream, PdfDocumentOpenMode.Import);
    }

    private static string ContentOf(PdfPage page)
    {
        var contents = page.Contents.Elements;

        return contents.Count == 0
            ? string.Empty
            : System.Text.Encoding.Latin1.GetString(
                contents.GetDictionary(0)!.Stream.UnfilteredValue);
    }

    private static byte[] Pdf(int[] marks)
    {
        using var document = new PdfDocument();

        foreach (var mark in marks)
        {
            Draw(document.AddPage(), mark);
        }

        return Bytes(document);
    }

    private static byte[] PdfWithTitle(string title)
    {
        using var document = new PdfDocument();
        document.Info.Title = title;
        Draw(document.AddPage(), 0);

        return Bytes(document);
    }

    /// <summary>
    /// Cada página recebe um retângulo em coordenada própria: é o que a identifica no fluxo de
    /// conteúdo, sem exigir resolvedor de fontes.
    /// </summary>
    private static void Draw(PdfPage page, int mark)
    {
        page.Size = PageSize.A4;

        using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);

        gfx.DrawRectangle(
            PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XRect(10 + mark, 20 + mark, 30, 40));
    }

    private static byte[] Bytes(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);

        return stream.ToArray();
    }
}
