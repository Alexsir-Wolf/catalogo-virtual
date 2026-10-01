namespace Catalogo.Features.Settings;

/// <summary>
/// O motivo pelo qual um arquivo é considerado inseguro para entregar ao parser.
/// </summary>
public enum PdfScanVerdict
{
    /// <summary>A varredura terminou e a profundidade ficou dentro do teto.</summary>
    Safe,

    /// <summary>Aninhamento acima de <see cref="PdfNestingScan.MaxDepth"/>.</summary>
    TooDeep,

    /// <summary>
    /// Mais fechamentos que aberturas. Sozinho isso seria só um arquivo torto — o que torna a
    /// recusa necessária é que um contador sem piso desce para valores negativos e **cancela** a
    /// medição do aninhamento que vem depois.
    /// </summary>
    UnbalancedDelimiters,

    /// <summary>
    /// Uma string, uma string hexadecimal ou um stream que começa e não termina. A varredura pula
    /// até o fim do arquivo e mede zero — ou seja, o guarda se desliga.
    /// </summary>
    UnterminatedToken
}

/// <summary>
/// Mede a profundidade de aninhamento sintático de um PDF **antes** de entregá-lo ao
/// PdfSharp.
///
/// Por que existe: o parser do PdfSharp desce recursivamente por dicionários e arrays
/// aninhados (`Parser.ReadArray` → `ParseObject` → `ReadArray`…). Um arquivo de **10 KB**
/// com um array de 5.000 níveis numa chave qualquer esgota a pilha e **mata o processo** —
/// sem ciclo, sem repetir objeto, com a árvore de páginas perfeitamente válida. Detecção de
/// ciclo não vê isso, e `StackOverflowException` não é capturável: não há `try/catch` que
/// segure (R-01 de `REVIEW-T-31-2026-09-29`, segunda rodada).
///
/// A varredura é léxica e de passada única: conta os delimitadores fora de strings,
/// comentários e streams. Não interpreta o arquivo, e é justamente por isso que é segura —
/// não tem recursão para estourar.
///
/// **O que a segunda rodada de review provou, e o que mudou por causa disso:** um guarda que
/// mede e não recusa o que não consegue medir é um guarda que se desliga. A versão anterior
/// tinha três saídas silenciosas, e cada uma bastava para o processo voltar a morrer:
///
/// - `depth--` sem piso. Seis mil `]` de lixo numa região que o parser nem visita levavam o
///   contador a -6000, e os 5.000 níveis reais depois disso nunca passavam do teto.
/// - `stream` sem `endstream`. A varredura pulava até o fim do arquivo e media zero.
/// - `(` sem `)`. **Um byte** e a varredura media zero.
///
/// Por isso o resultado agora não é um número, é um veredito: o que a varredura não consegue
/// medir com confiança é **recusado**, e não aceito por omissão. Um PDF bem formado não tem
/// delimitador desbalanceado nem token sem fim — recusar isso não custa capa legítima nenhuma.
///
/// **Limite conhecido, declarado:** objetos dentro de `/ObjStm` comprimido não são
/// alcançados, porque a varredura não descomprime nada. Fechar esse vetor pede a leitura
/// fora do processo que serve requisições, que é decisão de arquitetura e está registrada
/// como tarefa própria no plano.
/// </summary>
public static class PdfNestingScan
{
    /// <summary>
    /// Teto de profundidade. PDFs de produtores reais ficam em uma ou duas dezenas de
    /// níveis; o teto é folgado de propósito, porque recusar capa legítima é pior que
    /// aceitar um arquivo esquisito que o parser aguenta.
    /// </summary>
    public const int MaxDepth = 128;

    private const string StreamKeyword = "stream";

    private const string EndStreamKeyword = "endstream";

    private const byte Backslash = (byte)'\\';

    /// <summary>
    /// Verdadeiro quando o arquivo **não** deve ser entregue ao parser — por aninhamento acima do
    /// teto ou por a varredura não ter conseguido medi-lo.
    /// </summary>
    public static bool IsUnsafeToParse(byte[] content) => Scan(content) != PdfScanVerdict.Safe;

    /// <summary>
    /// O veredito da varredura. Público para que cada motivo tenha caso de teste próprio: os três
    /// que foram acrescentados nesta rodada eram, antes, o mesmo silêncio.
    /// </summary>
    public static PdfScanVerdict Scan(byte[] content)
    {
        var depth = 0;
        var index = 0;

        while (index < content.Length)
        {
            var current = content[index];

            switch (current)
            {
                case (byte)'%':
                    index = SkipComment(content, index);
                    continue;

                case (byte)'(':
                    if (!TrySkipLiteralString(content, index, out index))
                    {
                        return PdfScanVerdict.UnterminatedToken;
                    }

                    continue;

                case (byte)'<' when Next(content, index) == '<':
                    depth++;
                    index += 2;
                    break;

                case (byte)'<':
                    if (!TrySkipHexString(content, index, out index))
                    {
                        return PdfScanVerdict.UnterminatedToken;
                    }

                    continue;

                case (byte)'>' when Next(content, index) == '>':
                    depth--;
                    index += 2;
                    break;

                case (byte)'[':
                    depth++;
                    index++;
                    break;

                case (byte)']':
                    depth--;
                    index++;
                    break;

                case (byte)'s' when IsStreamKeyword(content, index):
                    if (!TrySkipStream(content, index, out index))
                    {
                        return PdfScanVerdict.UnterminatedToken;
                    }

                    continue;

                default:
                    index++;
                    break;
            }

            // O piso é a correção que fecha o contorno de um byte: sem ela, fechamento em excesso
            // empurrava o contador para baixo e comprava folga para o aninhamento seguinte.
            if (depth < 0)
            {
                return PdfScanVerdict.UnbalancedDelimiters;
            }

            if (depth > MaxDepth)
            {
                return PdfScanVerdict.TooDeep;
            }
        }

        return PdfScanVerdict.Safe;
    }

    private static char Next(byte[] content, int index) =>
        index + 1 < content.Length ? (char)content[index + 1] : '\0';

    private static int SkipComment(byte[] content, int index)
    {
        while (index < content.Length && content[index] is not ((byte)'\n' or (byte)'\r'))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// String literal. Os parênteses podem aninhar, e a barra invertida escapa o próximo
    /// byte — sem tratar isso, um `\)` terminaria a string cedo e delimitadores de dentro
    /// dela passariam a ser contados.
    ///
    /// Devolve falso quando a string não termina: é o contorno de um byte.
    /// </summary>
    private static bool TrySkipLiteralString(byte[] content, int index, out int next)
    {
        var open = 0;

        while (index < content.Length)
        {
            var current = content[index];

            if (current == Backslash)
            {
                index += 2;
                continue;
            }

            if (current == (byte)'(')
            {
                open++;
            }
            else if (current == (byte)')')
            {
                open--;

                if (open == 0)
                {
                    next = index + 1;

                    return true;
                }
            }

            index++;
        }

        next = content.Length;

        return false;
    }

    private static bool TrySkipHexString(byte[] content, int index, out int next)
    {
        index++;

        while (index < content.Length)
        {
            if (content[index] == (byte)'>')
            {
                next = index + 1;

                return true;
            }

            index++;
        }

        next = content.Length;

        return false;
    }

    /// <summary>
    /// `stream` só abre stream quando é **token**, e não quando é sufixo de outra coisa.
    ///
    /// Reconhecê-lo em qualquer posição era o quarto contorno desta varredura, e o mais barato:
    /// um nome como `/Xstream` fazia o salto até `endstream` engolir o aninhamento profundo que
    /// estava no meio, o veredito voltava `Safe`, e o parser — que não tem essa regra — lia o
    /// array e recursava até matar o processo (R-01 de `REVIEW-T-31-2026-09-30-round2`).
    ///
    /// A regra é a do PDF: o byte anterior tem de ser espaço ou delimitador. `/` **não** conta,
    /// porque dentro de um nome a palavra é só texto. Começo do arquivo não é stream: um PDF
    /// válido abre com `%PDF`, e `stream` solto na primeira posição não tem dicionário a que
    /// pertencer.
    /// </summary>
    private static bool IsStreamKeyword(byte[] content, int index)
    {
        if (!StartsWith(content, index, StreamKeyword))
        {
            return false;
        }

        if (index == 0)
        {
            return false;
        }

        var previous = content[index - 1];

        return previous is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t'
            or (byte)'\f' or 0 or (byte)'>' or (byte)']' or (byte)')';
    }

    /// <summary>
    /// Dados de stream são binários e podem conter qualquer byte, inclusive sequências que
    /// pareçam delimitadores. Contá-los produziria profundidade inventada.
    ///
    /// Devolve falso quando não há `endstream`: sem isso, os seis bytes da palavra `stream`
    /// desligavam a varredura.
    /// </summary>
    private static bool TrySkipStream(byte[] content, int index, out int next)
    {
        index += StreamKeyword.Length;

        while (index < content.Length)
        {
            if (content[index] == (byte)'e' && StartsWith(content, index, EndStreamKeyword))
            {
                next = index + EndStreamKeyword.Length;

                return true;
            }

            index++;
        }

        next = content.Length;

        return false;
    }

    private static bool StartsWith(byte[] content, int index, string word)
    {
        if (index + word.Length > content.Length)
        {
            return false;
        }

        for (var offset = 0; offset < word.Length; offset++)
        {
            if (content[index + offset] != (byte)word[offset])
            {
                return false;
            }
        }

        return true;
    }
}
