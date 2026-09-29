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
    IOptions<ObjectStorageOptions> options,
    ILogger<ProductPhotoService> logger)
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

    /// <summary>
    /// Remove o original e as quatro derivadas de uma foto (RN-20). É o **único** lugar do
    /// sistema que apaga imagem: substituir foto deixa a anterior para trás de propósito,
    /// porque um PDF já gerado ou uma página em cache ainda podem apontar para ela. Aqui o
    /// produto inteiro deixa de existir, então não há quem aponte.
    ///
    /// Falha em um objeto **não interrompe os outros**: o registro já saiu do acervo, e
    /// parar no meio deixaria mais arquivos órfãos, não menos. Cada falha vai para o log com
    /// o nome, que é o que permite limpar depois.
    /// </summary>
    public async Task DeleteAsync(ProductPhoto photo, CancellationToken cancellationToken = default)
    {
        foreach (var (bucket, objectName) in ObjectsOf(photo))
        {
            try
            {
                await storage.DeleteAsync(bucket, objectName, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    exception,
                    "Falha ao remover objeto de foto. Objeto sem referência: {Bucket}/{Nome}.",
                    bucket,
                    objectName);
            }
        }
    }

    /// <summary>
    /// Os cinco objetos de uma foto, cada um com o bucket em que vive. A derivada de
    /// impressão é a única privada (RN-12), e é por isso que o bucket vem junto do nome.
    /// </summary>
    private IEnumerable<(string Bucket, string ObjectName)> ObjectsOf(ProductPhoto photo)
    {
        // O original não é derivada e não tem especificação: é o arquivo como chegou, e vive
        // no bucket privado porque ninguém o consome pela vitrine.
        yield return (options.PrivateBucket, photo.OriginalFileName);

        // O bucket de cada derivada vem de `BucketFor`, a **mesma** função que o envio usa.
        // Repetir a regra aqui à mão faria a visibilidade divergir em silêncio no dia em que
        // uma derivada mudasse de lado — e o sintoma seria um DELETE no bucket errado, que
        // falha com 404 e vira objeto pago para sempre.
        foreach (var specification in DerivativeSpecifications.All)
        {
            yield return (BucketFor(specification), NameOf(photo, specification.Derivative));
        }
    }

    private static string NameOf(ProductPhoto photo, ImageDerivative derivative) => derivative switch
    {
        ImageDerivative.Thumbnail => photo.ThumbnailFileName,
        ImageDerivative.Card => photo.CardFileName,
        ImageDerivative.Large => photo.LargeFileName,
        ImageDerivative.Print => photo.PrintFileName,
        _ => throw new ArgumentOutOfRangeException(nameof(derivative), derivative, null)
    };

    private string BucketFor(DerivativeSpecification specification) =>
        specification.IsPublic ? options.PublicBucket : options.PrivateBucket;

    private static string ContentTypeFor(DerivativeSpecification specification) =>
        specification.Format == ImageFormat.Webp ? "image/webp" : "image/jpeg";

    private static string NameOf(
        IReadOnlyList<ProcessedDerivative> derivatives,
        ImageDerivative derivative) =>
        derivatives.Single(candidate => candidate.Specification.Derivative == derivative).ObjectName;
}
