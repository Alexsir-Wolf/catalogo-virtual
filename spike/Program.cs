using System.Diagnostics;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkiaSharp;
using Spike;

const string ReferenceCatalog = "../docs/prototype/assets/referencia-catalogo.pdf";
const string ExtractedDirectory = "saida/fotos";
const string DerivativeDirectory = "saida/impressao";
const string OutputPdf = "saida/catalogo-spike.pdf";
const int LongestSide = 800;
const int LogoWidth = 126;
const int BannerWidth = 1302;

QuestPDF.Settings.License = LicenseType.Community;

Console.WriteLine("1. Extraindo fotos reais do catálogo de referência");
var extracted = PhotoExtractor.ExtractTo(ReferenceCatalog, ExtractedDirectory);
var photos = extracted.Where(IsProductPhoto).ToList();
Console.WriteLine($"   {extracted.Count} imagens no PDF, {photos.Count} são fotos de produto");

Console.WriteLine($"\n2. Gerando derivadas de impressão em {LongestSide} px (ADR-005)");
var derivativeStopwatch = Stopwatch.StartNew();
var derivatives = photos.Select(photo => PrintDerivative.Generate(photo, DerivativeDirectory, LongestSide)).ToList();
derivativeStopwatch.Stop();

var originalBytes = photos.Sum(photo => new FileInfo(photo).Length);
var derivativeBytes = derivatives.Sum(derivative => new FileInfo(derivative).Length);
Console.WriteLine($"   {derivatives.Count} derivadas em {derivativeStopwatch.ElapsedMilliseconds} ms " +
                  $"({derivativeStopwatch.ElapsedMilliseconds / (double)derivatives.Count:F0} ms por foto)");
Console.WriteLine($"   original {originalBytes / 1024} KB → derivada {derivativeBytes / 1024} KB " +
                  $"(média {derivativeBytes / derivatives.Count / 1024} KB por produto)");

Console.WriteLine("\n3. Compondo o PDF");
var categories = Catalog.Build(derivatives);
var productCount = categories.Sum(category => category.Products.Count);

var compositionStopwatch = Stopwatch.StartNew();
new CatalogDocument(categories).GeneratePdf(OutputPdf);
compositionStopwatch.Stop();

var pdf = new FileInfo(OutputPdf);
Console.WriteLine($"   {productCount} produtos em {categories.Count} categorias");
Console.WriteLine($"   {compositionStopwatch.ElapsedMilliseconds} ms " +
                  $"({compositionStopwatch.ElapsedMilliseconds / (double)productCount:F0} ms por produto)");
Console.WriteLine($"   arquivo: {pdf.Length / 1024} KB ({pdf.Length / 1024.0 / 1024:F2} MB)");
Console.WriteLine($"   referência do cliente: 7.134 KB para 36 produtos");

Console.WriteLine("\n4. Comprimento das descrições usadas");
var descriptions = categories.SelectMany(category => category.Products)
    .Select(product => product.Description.Length)
    .Where(length => length > 0)
    .OrderBy(length => length)
    .ToList();
Console.WriteLine($"   mínimo {descriptions.First()} · mediana {descriptions[descriptions.Count / 2]} · máximo {descriptions.Last()} caracteres");

static bool IsProductPhoto(string path)
{
    using var bitmap = SKBitmap.Decode(path);
    return bitmap is not null && bitmap.Width != LogoWidth && bitmap.Width != BannerWidth;
}

Console.WriteLine("\n5. Capacidade da célula (medida, não estimada)");
var capacity = CellCapacity.Measure(
    pageMarginMillimetres: 14f,
    cellSpacingPoints: 8f,
    columnCount: 3,
    descriptionFontSize: 7.5f,
    maximumLines: 4,
    sampleDescriptions: categories.SelectMany(category => category.Products).Select(product => product.Description));

Console.WriteLine($"   largura da coluna: {capacity.ColumnWidthMillimetres:F1} mm (referência estimava 55 mm)");
Console.WriteLine($"   caracteres por linha: {capacity.CharactersPerLine}");
Console.WriteLine($"   em 4 linhas — o máximo observado no gabarito: {capacity.CharactersInMaximumLines} caracteres");
Console.WriteLine($"   maior descrição do spike que coube em 4 linhas: {capacity.LongestSampleFitting} caracteres");

Render.ToPng("saida/catalogo-spike.pdf", "saida/paginas");
Console.WriteLine("\n6. Páginas renderizadas em saida/paginas para inspeção");
