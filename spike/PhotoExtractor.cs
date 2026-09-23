using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Spike;

/// <summary>
/// Extrai as fotos de produto do catálogo de referência. O spike precisa medir com
/// material real — foto de catálogo comprime de forma muito diferente de imagem sintética.
/// </summary>
public static class PhotoExtractor
{
    private const int MinimumUsefulWidth = 120;

    public static IReadOnlyList<string> ExtractTo(string pdfPath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var extracted = new List<string>();
        using var document = PdfDocument.Open(pdfPath);

        foreach (var page in document.GetPages())
        {
            foreach (var image in page.GetImages())
            {
                var path = Save(image, outputDirectory, extracted.Count + 1);
                if (path is not null)
                {
                    extracted.Add(path);
                }
            }
        }

        return extracted;
    }

    private static string? Save(IPdfImage image, string outputDirectory, int sequence)
    {
        if (image.WidthInSamples < MinimumUsefulWidth)
        {
            return null;
        }

        var bytes = image.TryGetPng(out var png)
            ? png
            : image.RawBytes.ToArray();

        using var bitmap = SKBitmap.Decode(bytes);
        if (bitmap is null)
        {
            return null;
        }

        var path = Path.Combine(outputDirectory, $"foto-{sequence:D2}.png");
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);

        return path;
    }
}
