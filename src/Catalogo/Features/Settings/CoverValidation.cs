using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Catalogo.Features.Settings;

/// <summary>Por que uma capa foi recusada. <see cref="None"/> é aceitação.</summary>
public enum CoverRejection
{
    None,
    NotAPdf,
    PageCount,
    Landscape,
    AspectRatio,
    TooLarge,

    /// <summary>
    /// A árvore de páginas se refere a si mesma, ou é mais profunda do que qualquer
    /// documento legítimo. Ver <see cref="CoverValidation.MaxPageTreeNodes"/>.
    /// </summary>
    MalformedStructure
}

/// <summary>
/// Resultado da validação. <see cref="PagesFound"/> só é preenchido quando a recusa é por
/// número de páginas — a RN-63 exige informar **quantas** foram encontradas, e uma
/// mensagem genérica não cumpre isso.
/// </summary>
public sealed record CoverInspection(CoverRejection Rejection, int PagesFound = 0)
{
    public bool Accepted => Rejection == CoverRejection.None;

    public static CoverInspection Ok() => new(CoverRejection.None);
}

/// <summary>
/// Validação do arquivo de capa (RN-63, RN-64). Acontece **no envio**, não na geração: a
/// ADR-017 é explícita que divergência é recusada aqui, e descobrir no momento de gerar
/// significaria descobrir na frente do cliente.
///
/// O arquivo é entrada não confiável como qualquer upload, e por isso a validação lê a
/// **estrutura do PDF**, não a extensão nem o tipo declarado pelo navegador.
///
/// A árvore de páginas é percorrida **aqui**, e não pelas APIs `PageCount` e `Pages` do
/// PdfSharp. Elas achatam a árvore por recursão sem detecção de ciclo, e um nó `/Pages`
/// que se lista entre os próprios `/Kids` — 316 bytes bastam — esgota a pilha. Em .NET,
/// `StackOverflowException` **não é capturável**: nenhum `try/catch` em volta impede o
/// processo de morrer, e no Render o processo que serve o painel é o mesmo que serve a
/// vitrine (R-01 de `REVIEW-T-31-2026-09-29`). A caminhada abaixo é iterativa e guarda os
/// nós já vistos, o que resolve pela estrutura em vez de tentar tratar a exceção.
///
/// A leitura fica dentro do PdfSharp de propósito: um parser próprio não veria objetos
/// dentro de `/ObjStm` comprimido, e o ciclo passaria intacto.
/// </summary>
public static class CoverValidation
{
    /// <summary>
    /// 12 MB. A capa é uma página, e uma página de marketing com imagens vetoriais e
    /// fontes embutidas cabe com folga — o limite existe para barrar o acidente, não para
    /// apertar o uso legítimo.
    /// </summary>
    public const int MaxBytes = 12 * 1024 * 1024;

    /// <summary>
    /// As páginas de conteúdo são A4 retrato, cuja proporção é ~0,707 (210 por 297 mm).
    /// </summary>
    public const double ContentAspectRatio = 210d / 297d;

    /// <summary>
    /// Tolerância **relativa** à proporção do miolo — 6% dela, não 6 centésimos de
    /// proporção. A diferença importa: como valor absoluto, 0,06 equivalia a ~8,5%
    /// relativos e aceitava uma página de 210 por 276 mm, que sai com cerca de dois
    /// centímetros de faixa branca ao lado do conteúdo — exatamente o que a RN-64 existe
    /// para impedir. O registro dizia "6%" e a regra aplicada era outra (R-11 de
    /// `REVIEW-T-31-2026-09-29`).
    ///
    /// Carta em retrato continua recusada, e com folga: 0,774 está 9,5% fora do A4.
    /// Variação de milímetros no mesmo formato passa — 209 por 297 mm fica em 0,5%.
    /// </summary>
    public const double AspectRelativeTolerance = 0.06;

    /// <summary>
    /// Teto de nós da árvore de páginas. A capa tem uma página; documentos legítimos de
    /// centenas de páginas ficam muito abaixo disto, e o teto existe para o caso em que a
    /// árvore cresce sem repetir nó — que a detecção de ciclo não pega.
    /// </summary>
    public const int MaxPageTreeNodes = 4096;

    private const string RootPagesKey = "/Pages";

    private const string KidsKey = "/Kids";

    private const string MediaBoxKey = "/MediaBox";

    private const string RotateKey = "/Rotate";

    public static CoverInspection Inspect(byte[] content)
    {
        if (content.Length > MaxBytes)
        {
            return new CoverInspection(CoverRejection.TooLarge);
        }

        PdfDocument document;

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Qualquer falha de leitura é "não é um PDF que sabemos ler". Distinguir
            // arquivo corrompido de arquivo de outro tipo não mudaria nada para o dono:
            // em ambos os casos ele precisa enviar outro arquivo.
            return new CoverInspection(CoverRejection.NotAPdf);
        }

        using (document)
        {
            PageSurvey survey;

            try
            {
                survey = SurveyPages(document);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Árvore de páginas ilegível é o mesmo problema prático de arquivo que não
                // é PDF: o dono precisa enviar outro. Aqui o `catch` cobre o que ele pode
                // cobrir — o ciclo, que não é exceção, é barrado pela caminhada.
                return new CoverInspection(CoverRejection.NotAPdf);
            }

            if (survey.Malformed)
            {
                return new CoverInspection(CoverRejection.MalformedStructure);
            }

            if (survey.Pages != 1)
            {
                return new CoverInspection(CoverRejection.PageCount, survey.Pages);
            }

            var width = survey.Width;
            var height = survey.Height;

            if (width <= 0 || height <= 0)
            {
                return new CoverInspection(CoverRejection.NotAPdf);
            }

            if (width > height)
            {
                return new CoverInspection(CoverRejection.Landscape);
            }

            var drift = Math.Abs(width / height - ContentAspectRatio) / ContentAspectRatio;

            return drift > AspectRelativeTolerance
                ? new CoverInspection(CoverRejection.AspectRatio)
                : CoverInspection.Ok();
        }
    }

    /// <summary>
    /// Quantas páginas a árvore tem e as dimensões da primeira, já em pontos e na
    /// orientação em que o leitor vai exibir.
    /// </summary>
    private readonly record struct PageSurvey(
        int Pages,
        double Width,
        double Height,
        bool Malformed)
    {
        public static PageSurvey Broken() => new(0, 0, 0, Malformed: true);
    }

    /// <summary>
    /// Caminhada iterativa pela árvore de páginas, com os nós já visitados guardados por
    /// identificador de objeto. `MediaBox` e `Rotate` são herdáveis no formato, então
    /// descem com o nó em vez de serem lidos só na folha.
    /// </summary>
    private static PageSurvey SurveyPages(PdfDocument document)
    {
        if (document.Internals.Catalog.Elements.GetDictionary(RootPagesKey) is not { } root)
        {
            return PageSurvey.Broken();
        }

        var visited = new HashSet<PdfObjectID>();
        var pending = new Stack<InheritedNode>();
        pending.Push(new InheritedNode(root, Box: null, Rotation: 0));

        var pages = 0;
        var first = (Width: 0d, Height: 0d);

        while (pending.Count > 0)
        {
            if (visited.Count > MaxPageTreeNodes)
            {
                return PageSurvey.Broken();
            }

            var (node, inheritedBox, inheritedRotation) = pending.Pop();

            // Nó sem referência é objeto direto: não pode ser alvo de ciclo, mas também
            // não tem identidade para guardar.
            if (node.Reference is { } reference && !visited.Add(reference.ObjectID))
            {
                return PageSurvey.Broken();
            }

            var box = node.Elements.GetRectangle(MediaBoxKey) is { Width: > 0 } own
                ? own
                : inheritedBox;

            var rotation = node.Elements.ContainsKey(RotateKey)
                ? node.Elements.GetInteger(RotateKey)
                : inheritedRotation;

            if (node.Elements.GetArray(KidsKey) is not { } kids)
            {
                pages++;

                if (pages == 1 && box is { } leaf)
                {
                    first = Oriented(leaf, rotation);
                }

                continue;
            }

            for (var index = kids.Elements.Count - 1; index >= 0; index--)
            {
                if (kids.Elements.GetDictionary(index) is { } kid)
                {
                    pending.Push(new InheritedNode(kid, box, rotation));
                }
            }
        }

        return new PageSurvey(pages, first.Width, first.Height, Malformed: false);
    }

    /// <summary>
    /// Dimensões como o leitor exibe. Um quarto de volta troca largura por altura, e sem
    /// isso um A4 retrato girado passaria por retrato quando o papel sai em paisagem
    /// (RN-64).
    /// </summary>
    private static (double Width, double Height) Oriented(PdfRectangle box, int rotation)
    {
        var quarters = ((rotation % 360) + 360) % 360 / 90;

        return quarters % 2 == 0
            ? (box.Width, box.Height)
            : (box.Height, box.Width);
    }

    private readonly record struct InheritedNode(
        PdfDictionary Node,
        PdfRectangle? Box,
        int Rotation);

    /// <summary>
    /// A mensagem que a tela mostra. A recusa por páginas carrega a contagem porque a
    /// RN-63 manda informá-la — quem enviou o arquivo errado precisa saber o que enviou.
    /// </summary>
    public static string MessageFor(CoverInspection inspection) => inspection.Rejection switch
    {
        CoverRejection.NotAPdf =>
            "O arquivo não é um PDF que possamos ler. Envie o PDF original da capa.",
        CoverRejection.PageCount =>
            $"A capa precisa ter exatamente uma página, e este arquivo tem {inspection.PagesFound}.",
        CoverRejection.Landscape =>
            "A capa precisa estar em retrato, e este arquivo está em paisagem.",
        CoverRejection.AspectRatio =>
            "A proporção da capa não bate com as páginas de conteúdo, e o documento sairia "
            + "com faixa em branco ou corte. Use o mesmo tamanho de página do catálogo.",
        CoverRejection.TooLarge =>
            $"O arquivo passa de {MaxBytes / (1024 * 1024)} MB.",
        CoverRejection.MalformedStructure =>
            "A estrutura de páginas do arquivo está inconsistente e não pode ser lida com "
            + "segurança. Abra o PDF e salve novamente, ou exporte outra vez da origem.",
        _ => string.Empty
    };
}
