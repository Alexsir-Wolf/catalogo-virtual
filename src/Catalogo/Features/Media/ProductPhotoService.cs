using Catalogo.Features.Products;
using Microsoft.Extensions.Options;

namespace Catalogo.Features.Media;

public sealed record PhotoUploadResult(ProductPhoto? Photo, ImageRejection Rejection)
{
    public bool Succeeded => Photo is not null;
}

/// <summary>
/// Recebe a foto enviada, valida, gera as quatro derivadas e grava cada uma no bucket
/// correspondente. As três de tela vão para o bucket público; a de impressão, para o
/// privado (RN-11, RN-12, ADR-005, ADR-018).
/// </summary>
public sealed class ProductPhotoService(
    IObjectStorage storage,
    ImageProcessor processor,
    IOptions<ObjectStorageOptions> options)
{
    private readonly ObjectStorageOptions options = options.Value;

    public async Task<PhotoUploadResult> StoreAsync(
        Stream content,
        long lengthInBytes,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        buffer.Position = 0;
        var validation = ImageValidation.Validate(buffer, lengthInBytes);
        if (!validation.IsValid)
        {
            return new PhotoUploadResult(Photo: null, validation.Rejection);
        }

        buffer.Position = 0;
        var immutableName = ImageProcessor.NewImmutableName();
        var derivatives = processor.Process(buffer, immutableName);

        foreach (var derivative in derivatives)
        {
            await storage.UploadAsync(
                BucketFor(derivative.Specification),
                derivative.ObjectName,
                derivative.Content,
                ContentTypeFor(derivative.Specification),
                cancellationToken);
        }

        buffer.Position = 0;
        var originalName = $"{immutableName}-original";
        await storage.UploadAsync(
            options.PrivateBucket,
            originalName,
            buffer.ToArray(),
            "application/octet-stream",
            cancellationToken);

        return new PhotoUploadResult(
            new ProductPhoto
            {
                OriginalFileName = originalName,
                ThumbnailFileName = NameOf(derivatives, ImageDerivative.Thumbnail),
                CardFileName = NameOf(derivatives, ImageDerivative.Card),
                LargeFileName = NameOf(derivatives, ImageDerivative.Large),
                PrintFileName = NameOf(derivatives, ImageDerivative.Print)
            },
            ImageRejection.None);
    }

    public string PublicUrlFor(string objectName) => storage.PublicUrlFor(objectName);

    private string BucketFor(DerivativeSpecification specification) =>
        specification.IsPublic ? options.PublicBucket : options.PrivateBucket;

    private static string ContentTypeFor(DerivativeSpecification specification) =>
        specification.Format == ImageFormat.Webp ? "image/webp" : "image/jpeg";

    private static string NameOf(
        IReadOnlyList<ProcessedDerivative> derivatives,
        ImageDerivative derivative) =>
        derivatives.Single(candidate => candidate.Specification.Derivative == derivative).ObjectName;
}
