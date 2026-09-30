namespace Catalogo.Features.Media;

/// <summary>
/// As quatro derivadas da ADR-005. Três para tela, em WebP, e uma para impressão, em
/// JPEG — a única que o gerador de PDF consome e que nunca é exposta publicamente.
/// </summary>
public enum ImageDerivative
{
    Thumbnail,
    Card,
    Large,
    Print
}

public sealed record DerivativeSpecification(
    ImageDerivative Derivative,
    int LongestSide,
    ImageFormat Format,
    int Quality)
{
    public bool IsPublic => Derivative != ImageDerivative.Print;

    public string Extension => Format == ImageFormat.Webp ? "webp" : "jpg";
}

public enum ImageFormat
{
    Webp,
    Jpeg
}

public static class DerivativeSpecifications
{
    /// <summary>
    /// Lado maior da derivada de impressão. **Número fechado por T-04**, contra impressão real: os
    /// 800 px que a ADR-005 declarava como ponto de partida foram confirmados no papel e deixaram
    /// de ser provisórios. Quem mexer aqui está mexendo numa medição, não numa estimativa — e o
    /// efeito só aparece impresso, depois de o acervo inteiro já ter sido processado.
    /// </summary>
    public const int PrintLongestSide = 800;

    public static readonly IReadOnlyList<DerivativeSpecification> All =
    [
        new(ImageDerivative.Thumbnail, LongestSide: 160, ImageFormat.Webp, Quality: 80),
        new(ImageDerivative.Card, LongestSide: 480, ImageFormat.Webp, Quality: 82),
        new(ImageDerivative.Large, LongestSide: 1200, ImageFormat.Webp, Quality: 85),
        new(ImageDerivative.Print, PrintLongestSide, ImageFormat.Jpeg, Quality: 88)
    ];
}
