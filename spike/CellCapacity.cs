using SkiaSharp;

namespace Spike;

/// <summary>
/// Mede quantos caracteres de resumo cabem na célula da grade, a partir da largura real
/// da coluna e das métricas da fonte usada na composição. Substitui estimativa por medida.
/// </summary>
public static class CellCapacity
{
    private const float A4WidthMillimetres = 210f;
    private const float PointsPerMillimetre = 72f / 25.4f;

    public static CellCapacityReport Measure(
        float pageMarginMillimetres,
        float cellSpacingPoints,
        int columnCount,
        float descriptionFontSize,
        int maximumLines,
        IEnumerable<string> sampleDescriptions)
    {
        var pageWidth = A4WidthMillimetres * PointsPerMillimetre;
        var usable = pageWidth - (2 * pageMarginMillimetres * PointsPerMillimetre) - (cellSpacingPoints * columnCount);
        var columnWidth = usable / columnCount;

        using var font = new SKFont(SKTypeface.Default, descriptionFontSize);
        var averageCharacterWidth = font.MeasureText(SampleAlphabet) / SampleAlphabet.Length;

        var charactersPerLine = (int)Math.Floor(columnWidth / averageCharacterWidth);

        var longestFitting = sampleDescriptions
            .Select(description => new
            {
                Length = description.Length,
                Lines = (int)Math.Ceiling(font.MeasureText(description) / columnWidth)
            })
            .Where(sample => sample.Lines <= maximumLines)
            .OrderByDescending(sample => sample.Length)
            .FirstOrDefault();

        return new CellCapacityReport(
            ColumnWidthMillimetres: columnWidth / PointsPerMillimetre,
            CharactersPerLine: charactersPerLine,
            CharactersInMaximumLines: charactersPerLine * maximumLines,
            LongestSampleFitting: longestFitting?.Length ?? 0);
    }

    private const string SampleAlphabet =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ, .0123456789";
}

public sealed record CellCapacityReport(
    float ColumnWidthMillimetres,
    int CharactersPerLine,
    int CharactersInMaximumLines,
    int LongestSampleFitting);
