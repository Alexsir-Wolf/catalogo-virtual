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
    /// Carta em retrato **é recusada**, e de propósito: a proporção dela é 0,773 contra
    /// 0,707 do A4 — 6,6% de diferença, que no papel é faixa branca ou corte visível ao
    /// lado do miolo. É exatamente o que a RN-64 existe para impedir, e a ADR-017 manda
    /// recusar no envio em vez de deixar aparecer na impressão.
    /// </summary>
    [Fact]
    public void RN_64_carta_em_retrato_e_recusada_por_proporcao()
    {
        var inspection = CoverValidation.Inspect(Pdf(PageSize.Letter, pages: 1));

        Assert.Equal(CoverRejection.AspectRatio, inspection.Rejection);
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
