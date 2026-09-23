using SkiaSharp;

namespace Catalogo.Features.Media;

public sealed record ProcessedDerivative(
    DerivativeSpecification Specification,
    string ObjectName,
    byte[] Content);

/// <summary>
/// Produz as quatro derivadas da ADR-005 em memória. O disco da aplicação é efêmero na
/// plataforma (ADR-018), então nada aqui toca o sistema de arquivos — nem como passo
/// intermediário.
/// </summary>
public sealed class ImageProcessor
{
    /// <summary>
    /// Raiz imutável e única por envio. Reenviar a foto produz outra raiz, e por isso as
    /// derivadas anteriores nunca são sobrescritas (RN-13).
    /// </summary>
    public static string NewImmutableName() => Guid.NewGuid().ToString("n");

    public IReadOnlyList<ProcessedDerivative> Process(Stream content, string immutableName)
    {
        // Como o SKCodec, o decodificador fecha o stream que recebe. O envoltório sem
        // posse deixa o buffer utilizável por quem chamou.
        using var borrowed = new SKManagedStream(content, disposeManagedStream: false);

        using var source = SKBitmap.Decode(borrowed)
            ?? throw new InvalidOperationException("O conteúdo não pôde ser decodificado como imagem.");

        return DerivativeSpecifications.All
            .Select(specification => Render(source, specification, immutableName))
            .ToList();
    }

    private static ProcessedDerivative Render(
        SKBitmap source,
        DerivativeSpecification specification,
        string immutableName)
    {
        var scale = (float)specification.LongestSide / Math.Max(source.Width, source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * Math.Min(scale, 1f)));
        var height = Math.Max(1, (int)Math.Round(source.Height * Math.Min(scale, 1f)));

        using var resized = source.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(ToSkiaFormat(specification.Format), specification.Quality);

        var objectName = ObjectNameFor(immutableName, specification);

        return new ProcessedDerivative(specification, objectName, encoded.ToArray());
    }

    public static string ObjectNameFor(string immutableName, DerivativeSpecification specification) =>
        $"{immutableName}-{specification.Derivative.ToString().ToLowerInvariant()}.{specification.Extension}";

    private static SKEncodedImageFormat ToSkiaFormat(ImageFormat format) => format switch
    {
        ImageFormat.Webp => SKEncodedImageFormat.Webp,
        ImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
