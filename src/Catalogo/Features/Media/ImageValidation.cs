using SkiaSharp;

namespace Catalogo.Features.Media;

public enum ImageRejection
{
    None,
    TooLarge,
    NotAnImage,
    DimensionsTooLarge,
    DimensionsTooSmall
}

public sealed record ImageValidationResult(ImageRejection Rejection)
{
    public bool IsValid => Rejection == ImageRejection.None;

    public static readonly ImageValidationResult Valid = new(ImageRejection.None);
}

/// <summary>
/// Valida o arquivo enviado pelo <b>conteúdo</b>, nunca pela extensão (RN-10). Um `.jpg`
/// que não decodifica como imagem é recusado, e é exatamente esse o caso do CA-06.
/// </summary>
public static class ImageValidation
{
    public const long MaxFileSizeInBytes = 12 * 1024 * 1024;

    public const int MaxDimension = 6000;

    public const int MinDimension = 200;

    public static ImageValidationResult Validate(Stream content, long lengthInBytes)
    {
        if (lengthInBytes > MaxFileSizeInBytes)
        {
            return new ImageValidationResult(ImageRejection.TooLarge);
        }

        using var codec = SKCodec.Create(content);
        if (codec is null)
        {
            return new ImageValidationResult(ImageRejection.NotAnImage);
        }

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            return new ImageValidationResult(ImageRejection.NotAnImage);
        }

        if (info.Width > MaxDimension || info.Height > MaxDimension)
        {
            return new ImageValidationResult(ImageRejection.DimensionsTooLarge);
        }

        if (info.Width < MinDimension || info.Height < MinDimension)
        {
            return new ImageValidationResult(ImageRejection.DimensionsTooSmall);
        }

        return ImageValidationResult.Valid;
    }
}
