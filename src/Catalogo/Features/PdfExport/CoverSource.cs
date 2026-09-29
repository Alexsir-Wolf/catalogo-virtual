using Catalogo.Features.Media;
using Microsoft.Extensions.Options;

namespace Catalogo.Features.PdfExport;

/// <summary>
/// De onde vem o arquivo da capa na hora de concatenar.
///
/// Existe como abstração fina por um motivo prático: a capa vive no armazenamento de objeto
/// (ADR-018), e é o único insumo do documento final que não é composto pelo sistema — poder
/// substituí-la é o que torna a concatenação testável sem credencial.
/// </summary>
public interface ICoverSource
{
    Task<byte[]> DownloadAsync(string objectName, CancellationToken cancellationToken = default);
}

public sealed class StoredCoverSource(
    IObjectStorage storage,
    IOptions<ObjectStorageOptions> options) : ICoverSource
{
    public Task<byte[]> DownloadAsync(
        string objectName,
        CancellationToken cancellationToken = default) =>
        storage.DownloadAsync(options.Value.PublicBucket, objectName, cancellationToken);
}
