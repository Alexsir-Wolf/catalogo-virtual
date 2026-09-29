using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Catalogo.Features.PdfExport;

/// <summary>
/// Une a capa enviada pelo dono às páginas compostas pelo sistema (RN-36, ADR-017).
///
/// **A capa entra sem nenhuma alteração.** O sistema não escreve nada sobre ela (RN-37): nem
/// numeração, nem cabeçalho, nem marca. É o arquivo do dono, na frente do documento, e a
/// numeração do miolo já nasce contando essa folha (RN-40.1).
///
/// A biblioteca é o **PDFsharp**, que já entrou no projeto em T-31 para validar a capa — a
/// ADR-017 a nomeia, e ela tem licença MIT, sem a restrição de porte que a ADR-012 carrega no
/// QuestPDF. Uma dependência serve aos dois usos em vez de duas.
/// </summary>
public static class CoverMerge
{
    /// <summary>
    /// Capa primeiro, conteúdo depois. O documento resultante é novo: as páginas são importadas
    /// para ele, o que é o que produz um arquivo que abre em leitor comum — costurar os bytes
    /// dos dois originais geraria um PDF que só alguns leitores aceitam.
    /// </summary>
    public static byte[] Merge(byte[] cover, byte[] content)
    {
        using var merged = new PdfDocument();

        Append(merged, cover);
        Append(merged, content);

        // Sem metadados herdados de nenhum dos dois: o produtor do arquivo final é este sistema,
        // e carregar o do editor que fez a capa confundiria quem for inspecionar o documento.
        merged.Info.Title = string.Empty;
        merged.Info.Creator = string.Empty;

        using var output = new MemoryStream();
        merged.Save(output, closeStream: false);

        return output.ToArray();
    }

    /// <summary>
    /// `Import` é o modo que permite copiar páginas. `Modify` falharia em PDF protegido por
    /// senha de dono, que é aceito na validação da capa justamente porque este caminho funciona
    /// — verificado ao revisar T-31.
    /// </summary>
    private static void Append(PdfDocument target, byte[] source)
    {
        using var stream = new MemoryStream(source, writable: false);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        for (var index = 0; index < document.PageCount; index++)
        {
            target.AddPage(document.Pages[index]);
        }
    }
}
