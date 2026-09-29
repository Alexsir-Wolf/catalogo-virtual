using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Catalogo.Features.Settings;

/// <summary>Por que uma capa foi recusada. <see cref="None"/> é aceitação.</summary>
public enum CoverRejection
{
    None,
    NotAPdf,
    PageCount,
    Landscape,
    AspectRatio,
    TooLarge
}

/// <summary>
/// Resultado da validação. <see cref="PagesFound"/> só é preenchido quando a recusa é por
/// número de páginas — a RN-63 exige informar **quantas** foram encontradas, e uma
/// mensagem genérica não cumpre isso.
/// </summary>
public sealed record CoverInspection(CoverRejection Rejection, int PagesFound = 0)
{
    public bool Accepted => Rejection == CoverRejection.None;

    public static CoverInspection Ok() => new(CoverRejection.None);
}

/// <summary>
/// Validação do arquivo de capa (RN-63, RN-64). Acontece **no envio**, não na geração: a
/// ADR-017 é explícita que divergência é recusada aqui, e descobrir no momento de gerar
/// significaria descobrir na frente do cliente.
///
/// O arquivo é entrada não confiável como qualquer upload, e por isso a validação lê a
/// **estrutura do PDF**, não a extensão nem o tipo declarado pelo navegador.
/// </summary>
public static class CoverValidation
{
    /// <summary>
    /// 12 MB. A capa é uma página, e uma página de marketing com imagens vetoriais e
    /// fontes embutidas cabe com folga — o limite existe para barrar o acidente, não para
    /// apertar o uso legítimo.
    /// </summary>
    public const int MaxBytes = 12 * 1024 * 1024;

    /// <summary>
    /// As páginas de conteúdo são A4 retrato, cuja proporção é ~0,707 (210 por 297 mm). A
    /// tolerância cobre variação de milímetros no mesmo formato. Carta fica fora: 0,773
    /// contra 0,707 é 6,6% de diferença, e no papel isso é faixa branca ou corte visível
    /// ao lado do miolo (RN-64).
    /// </summary>
    public const double ContentAspectRatio = 210d / 297d;

    public const double AspectTolerance = 0.06;

    public static CoverInspection Inspect(byte[] content)
    {
        if (content.Length > MaxBytes)
        {
            return new CoverInspection(CoverRejection.TooLarge);
        }

        PdfDocument document;

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            document = PdfReader.Open(stream, PdfDocumentOpenMode.InformationOnly);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Qualquer falha de leitura é "não é um PDF que sabemos ler". Distinguir
            // arquivo corrompido de arquivo de outro tipo não mudaria nada para o dono:
            // em ambos os casos ele precisa enviar outro arquivo.
            return new CoverInspection(CoverRejection.NotAPdf);
        }

        using (document)
        {
            if (document.PageCount != 1)
            {
                return new CoverInspection(CoverRejection.PageCount, document.PageCount);
            }

            var page = document.Pages[0];
            var width = page.Width.Point;
            var height = page.Height.Point;

            if (width <= 0 || height <= 0)
            {
                return new CoverInspection(CoverRejection.NotAPdf);
            }

            if (width > height)
            {
                return new CoverInspection(CoverRejection.Landscape);
            }

            return Math.Abs(width / height - ContentAspectRatio) > AspectTolerance
                ? new CoverInspection(CoverRejection.AspectRatio)
                : CoverInspection.Ok();
        }
    }

    /// <summary>
    /// A mensagem que a tela mostra. A recusa por páginas carrega a contagem porque a
    /// RN-63 manda informá-la — quem enviou o arquivo errado precisa saber o que enviou.
    /// </summary>
    public static string MessageFor(CoverInspection inspection) => inspection.Rejection switch
    {
        CoverRejection.NotAPdf =>
            "O arquivo não é um PDF que possamos ler. Envie o PDF original da capa.",
        CoverRejection.PageCount =>
            $"A capa precisa ter exatamente uma página, e este arquivo tem {inspection.PagesFound}.",
        CoverRejection.Landscape =>
            "A capa precisa estar em retrato, e este arquivo está em paisagem.",
        CoverRejection.AspectRatio =>
            "A proporção da capa não bate com as páginas de conteúdo, e o documento sairia "
            + "com faixa em branco ou corte. Use o mesmo tamanho de página do catálogo.",
        CoverRejection.TooLarge =>
            $"O arquivo passa de {MaxBytes / (1024 * 1024)} MB.",
        _ => string.Empty
    };
}
