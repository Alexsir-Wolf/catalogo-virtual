using PDFtoImage;
using SkiaSharp;

namespace Spike;

/// <summary>Renderiza as páginas do PDF em PNG — existe só para inspeção visual do spike.</summary>
public static class Render
{
    public static void ToPng(string pdfPath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var bytes = File.ReadAllBytes(pdfPath);
        var page = 1;

        foreach (var image in Conversion.ToImages(bytes, options: new RenderOptions(Dpi: 110)))
        {
            using (image)
            using (var data = image.Encode(SKEncodedImageFormat.Png, 90))
            using (var stream = File.Create(Path.Combine(outputDirectory, $"pagina-{page:D2}.png")))
            {
                data.SaveTo(stream);
            }

            page++;
        }
    }
}
