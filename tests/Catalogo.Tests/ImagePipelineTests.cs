using System.Text;
using Catalogo.Features.Media;
using SkiaSharp;

namespace Catalogo.Tests;

public sealed class ImagePipelineTests
{
    [Fact]
    public void CA_06_arquivo_mascarado_por_extensao_e_recusado()
    {
        // Conteúdo de texto com nome de imagem: a validação olha o conteúdo, não o nome.
        var content = new MemoryStream(Encoding.UTF8.GetBytes("isto não é uma imagem"));

        var result = ImageValidation.Validate(content, content.Length);

        Assert.Equal(ImageRejection.NotAnImage, result.Rejection);
    }

    [Fact]
    public void Arquivo_acima_do_limite_de_tamanho_e_recusado()
    {
        using var image = CreateImage(800, 600);

        var result = ImageValidation.Validate(image, ImageValidation.MaxFileSizeInBytes + 1);

        Assert.Equal(ImageRejection.TooLarge, result.Rejection);
    }

    [Fact]
    public void Imagem_acima_do_limite_de_dimensao_e_recusada()
    {
        using var image = CreateImage(ImageValidation.MaxDimension + 10, 500);

        var result = ImageValidation.Validate(image, image.Length);

        Assert.Equal(ImageRejection.DimensionsTooLarge, result.Rejection);
    }

    [Fact]
    public void Imagem_valida_e_aceita()
    {
        using var image = CreateImage(1000, 750);

        var result = ImageValidation.Validate(image, image.Length);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Upload_valido_gera_exatamente_quatro_derivadas()
    {
        using var image = CreateImage(1600, 1200);

        var derivatives = new ImageProcessor().Process(image, ImageProcessor.NewImmutableName());

        Assert.Equal(4, derivatives.Count);
        Assert.Equal(
            [ImageDerivative.Thumbnail, ImageDerivative.Card, ImageDerivative.Large, ImageDerivative.Print],
            derivatives.Select(derivative => derivative.Specification.Derivative));
    }

    [Fact]
    public void Derivada_de_impressao_e_jpeg_e_as_de_tela_sao_webp()
    {
        using var image = CreateImage(1600, 1200);

        var derivatives = new ImageProcessor().Process(image, ImageProcessor.NewImmutableName());

        var print = derivatives.Single(d => d.Specification.Derivative == ImageDerivative.Print);
        Assert.EndsWith(".jpg", print.ObjectName);
        Assert.False(print.Specification.IsPublic);

        Assert.All(
            derivatives.Where(d => d.Specification.Derivative != ImageDerivative.Print),
            derivative =>
            {
                Assert.EndsWith(".webp", derivative.ObjectName);
                Assert.True(derivative.Specification.IsPublic);
            });
    }

    [Fact]
    public void Derivada_de_impressao_respeita_o_lado_maior_definido()
    {
        using var image = CreateImage(2400, 1200);

        var derivatives = new ImageProcessor().Process(image, ImageProcessor.NewImmutableName());
        var print = derivatives.Single(d => d.Specification.Derivative == ImageDerivative.Print);

        using var decoded = SKBitmap.Decode(print.Content);

        Assert.Equal(DerivativeSpecifications.PrintLongestSide, Math.Max(decoded.Width, decoded.Height));
    }

    [Fact]
    public void RN_13_reenviar_gera_nomes_novos_sem_sobrescrever_os_anteriores()
    {
        using var first = CreateImage(1000, 800);
        using var second = CreateImage(1000, 800);
        var processor = new ImageProcessor();

        var before = processor.Process(first, ImageProcessor.NewImmutableName());
        var after = processor.Process(second, ImageProcessor.NewImmutableName());

        Assert.Empty(
            before.Select(d => d.ObjectName).Intersect(after.Select(d => d.ObjectName)));
    }

    private static MemoryStream CreateImage(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return new MemoryStream(encoded.ToArray());
    }
}
