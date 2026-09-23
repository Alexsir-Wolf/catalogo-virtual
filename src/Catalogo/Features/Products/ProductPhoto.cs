namespace Catalogo.Features.Products;

/// <summary>
/// Referências das derivadas geradas a partir da foto enviada (RN-11). Cada produto tem
/// uma foto só — não há galeria (RN-09) —, então isto é um bloco de colunas do próprio
/// produto, não uma tabela à parte.
/// </summary>
public class ProductPhoto
{
    public const int FileNameMaxLength = 120;

    public required string OriginalFileName { get; set; }

    public required string ThumbnailFileName { get; set; }

    public required string CardFileName { get; set; }

    public required string LargeFileName { get; set; }

    /// <summary>Derivada de impressão, consumida apenas pela geração do PDF (RN-12).</summary>
    public required string PrintFileName { get; set; }
}
