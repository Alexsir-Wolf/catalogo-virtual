using SkiaSharp;

namespace Spike;

/// <summary>
/// Produz a derivada de impressão da ADR-005: JPEG com o lado maior em torno de 800 px.
/// O valor é premissa a confirmar em T-04, por isso é parâmetro e não constante.
/// </summary>
public static class PrintDerivative
{
    private const int JpegQuality = 88;

    public static string Generate(string sourcePath, string outputDirectory, int longestSide)
    {
        Directory.CreateDirectory(outputDirectory);

        using var source = SKBitmap.Decode(sourcePath);
        var scale = (float)longestSide / Math.Max(source.Width, source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var resized = source.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

        var path = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(sourcePath)}.jpg");
        using var stream = File.Create(path);
        encoded.SaveTo(stream);

        return path;
    }
}
