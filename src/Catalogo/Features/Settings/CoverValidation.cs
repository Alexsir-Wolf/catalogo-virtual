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
///
/// <see cref="PageCountTruncated"/> diz que a contagem **parou no teto**. Sem essa distinção o teto
/// entrava na mensagem como se fosse o total: um PDF de 300 páginas produzia "este arquivo tem 65",
/// e o cenário é o erro mais provável desta tela — o dono envia o catálogo inteiro no lugar da capa
/// e recebe um número inventado, que é o oposto do que a RN-63 pede.
/// </summary>
public sealed record CoverInspection(
    CoverRejection Rejection,
    int PagesFound = 0,
    bool PageCountTruncated = false)
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
    /// 4 MB. A capa é **uma página**, e uma folha de marketing com imagem de fundo em alta e fontes
    /// embutidas fica bem abaixo disso — o limite existe para barrar o acidente, não para apertar o
    /// uso legítimo.
    ///
    /// **Era 12 MB, e o limite de tamanho não é limite de custo.** O review mediu: um PDF válido de
    /// 11,5 MB com cento e cinquenta mil dicionários triviais é **aceito**, e a leitura custa dois
    /// segundos de CPU e 202 MB de pico de memória — porque o modo de abertura materializa todos os
    /// objetos indiretos antes de qualquer decisão. Num contêiner pequeno (ADR-018) o encerramento
    /// por falta de memória não gera exceção nem log: o sintoma é indistinguível de um reinício
    /// qualquer, e leva a vitrine pública com ele.
    ///
    /// Reduzir o teto não resolve a classe do problema, e não finge resolver: a saída definitiva é
    /// ler o arquivo fora do processo que serve requisições, e isso está registrado como pendência.
    /// O que este número faz é diminuir a janela pelo fator que está ao alcance.
    /// </summary>
    public const int MaxBytes = 4 * 1024 * 1024;

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

    /// <summary>
    /// A partir daqui a contagem para: já se sabe que não é uma página, e a mensagem de
    /// recusa não precisa do número exato quando ele é absurdo.
    /// </summary>
    public const int MaxPagesToCount = 64;

    /// <summary>
    /// Teto de lado da página, em pontos — é o limite do próprio formato PDF (200 polegadas).
    /// Sem ele, `MediaBox` de proporção perfeita e dimensões absurdas passava: uma página de
    /// dezenas de quilômetros seria aceita e iria para a concatenação de T-32.
    /// </summary>
    public const double MaxSidePoints = 14_400;

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

        // **Antes** de o arquivo tocar o PdfSharp: o parser dele desce recursivamente por
        // dicionários e arrays, e 10 KB com 5.000 níveis de aninhamento esgotam a pilha e
        // matam o processo. A caminhada iterativa abaixo protege a travessia da árvore de
        // páginas; ela não protege o parse, que acontece antes (R-01, segunda rodada).
        if (PdfNestingScan.IsUnsafeToParse(content))
        {
            return new CoverInspection(CoverRejection.MalformedStructure);
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
                return new CoverInspection(CoverRejection.PageCount, survey.Pages, survey.Truncated);
            }

            var width = survey.Width;
            var height = survey.Height;

            if (width <= 0 || height <= 0)
            {
                return new CoverInspection(CoverRejection.NotAPdf);
            }

            if (width > MaxSidePoints || height > MaxSidePoints)
            {
                // Proporção certa não basta: uma página fora do limite do formato é estrutura
                // inválida, e passaria por ser proporcional ao A4.
                return new CoverInspection(CoverRejection.MalformedStructure);
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
        bool Malformed,

        /// <summary>
        /// Verdadeiro quando a contagem parou no teto em vez de terminar. Sem esta marca, o teto
        /// entrava na mensagem como se fosse o total real — um PDF de 300 páginas produzia "este
        /// arquivo tem 65", que é o oposto do que a RN-63 pede.
        /// </summary>
        bool Truncated = false)
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
        var seen = 0;
        var first = (Width: 0d, Height: 0d);

        while (pending.Count > 0)
        {
            // O teto conta **nós visitados**, não nós com identidade. Contra `visited.Count`
            // ele era cego para dicionários diretos: 300 mil páginas diretas num `/Kids`
            // cabem em 11 MB, nunca tocavam o teto, e a fila sozinha custava mais de 300 MB
            // de memória num container pequeno (ADR-018).
            if (++seen > MaxPageTreeNodes)
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

            // A presença da caixa é medida em **valor absoluto**, como `Oriented` já fazia: o
            // formato permite os cantos em qualquer ordem, e `MediaBox[595 0 0 842]` tem largura
            // negativa. Exigir `Width > 0` descartava essa caixa como ausente, a herança devolvia
            // nulo, e a página caía no ramo de dimensão inválida — recusada com "o arquivo não é um
            // PDF que possamos ler", que é a mensagem enganosa que o próprio comentário de
            // `Oriented` diz ter corrigido. Com X **e** Y invertidos já funcionava, o que deixava o
            // defeito parecendo tratado.
            var box = node.Elements.GetRectangle(MediaBoxKey) is { } own && HasArea(own)
                ? own
                : inheritedBox;

            var rotation = node.Elements.ContainsKey(RotateKey)
                ? node.Elements.GetInteger(RotateKey)
                : inheritedRotation;

            if (!IsValidRotation(rotation))
            {
                return PageSurvey.Broken();
            }

            if (node.Elements.GetArray(KidsKey) is not { } kids)
            {
                pages++;

                if (pages == 1 && box is { } leaf)
                {
                    first = Oriented(leaf, rotation);
                }

                // A capa tem uma página. Contar até o fim de uma árvore de centenas de
                // milhares de folhas só para dizer "não é uma" é trabalho jogado fora, e a
                // contagem exata não entra na mensagem quando passa do teto.
                if (pages > MaxPagesToCount)
                {
                    return new PageSurvey(
                        pages,
                        first.Width,
                        first.Height,
                        Malformed: false,
                        Truncated: true);
                }

                continue;
            }

            for (var index = kids.Elements.Count - 1; index >= 0; index--)
            {
                if (pending.Count + seen > MaxPageTreeNodes)
                {
                    return PageSurvey.Broken();
                }

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
    /// <summary>
    /// A caixa tem área, medida em valor absoluto. É o mesmo critério que <see cref="Oriented"/>
    /// aplica, e ter os dois divergindo foi o que produziu a recusa enganosa: um exigia largura
    /// positiva para reconhecer a caixa, o outro lia a dimensão em módulo.
    /// </summary>
    private static bool HasArea(PdfRectangle box) =>
        Math.Abs(box.Width) > 0 && Math.Abs(box.Height) > 0;

    private static (double Width, double Height) Oriented(PdfRectangle box, int rotation)
    {
        // Os cantos da caixa podem vir em qualquer ordem — o formato permite, e ler a
        // dimensão como negativa recusava uma capa legítima dizendo "não é um PDF".
        var width = Math.Abs(box.Width);
        var height = Math.Abs(box.Height);

        var quarters = ((rotation % 360) + 360) % 360 / 90;

        return quarters % 2 == 0
            ? (width, height)
            : (height, width);
    }

    /// <summary>
    /// `/Rotate` é definido em múltiplos de 90 pelo formato. Valor fora disso era tratado
    /// como zero pela divisão inteira — `/Rotate 45` passava como retrato.
    /// </summary>
    private static bool IsValidRotation(int rotation) => rotation % 90 == 0;

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
        CoverRejection.PageCount when inspection.PageCountTruncated =>
            $"A capa precisa ter exatamente uma página, e este arquivo tem mais de "
            + $"{MaxPagesToCount}. Envie só a folha da capa, não o catálogo inteiro.",
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
