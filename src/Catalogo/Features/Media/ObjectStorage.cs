namespace Catalogo.Features.Media;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = string.Empty;

    public string ServiceKey { get; set; } = string.Empty;

    /// <summary>Bucket público: as três derivadas de tela.</summary>
    public string PublicBucket { get; set; } = "produtos-web";

    /// <summary>Bucket privado: apenas a derivada de impressão (RN-12, CA-28).</summary>
    public string PrivateBucket { get; set; } = "produtos-print";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ServiceKey);
}

public interface IObjectStorage
{
    Task UploadAsync(
        string bucket,
        string objectName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string bucket, string objectName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lê um objeto de volta. Existe para a composição do PDF (T-24), que consome a derivada
    /// **de impressão** — ela vive no bucket privado (RN-12) e por isso não pode ser buscada
    /// por URL pública como as derivadas de tela.
    /// </summary>
    Task<byte[]> DownloadAsync(
        string bucket,
        string objectName,
        CancellationToken cancellationToken = default);

    string PublicUrlFor(string objectName);
}
