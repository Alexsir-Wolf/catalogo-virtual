namespace Catalogo.Features.Settings;

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

    private const byte Backslash = (byte)'\\';

    public static bool ExceedsMaxDepth(byte[] content) => MaxDepthOf(content) > MaxDepth;

    /// <summary>
    /// A maior profundidade de `&lt;&lt;` e `[` encontrada. Para de contar assim que passa do
    /// teto: saber o quanto passou não muda a decisão.
    /// </summary>
    public static int MaxDepthOf(byte[] content)
    {
        var depth = 0;
        var deepest = 0;
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
                    index = SkipLiteralString(content, index);
                    continue;

                case (byte)'<' when Next(content, index) == '<':
                    depth++;
                    index += 2;
                    break;

                case (byte)'<':
                    index = SkipHexString(content, index);
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

                case (byte)'s' when StartsWith(content, index, "stream"):
                    index = SkipStream(content, index);
                    continue;

                default:
                    index++;
                    break;
            }

            if (depth > deepest)
            {
                deepest = depth;

                if (deepest > MaxDepth)
                {
                    return deepest;
                }
            }
        }

        return deepest;
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
    /// </summary>
    private static int SkipLiteralString(byte[] content, int index)
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
                    return index + 1;
                }
            }

            index++;
        }

        return index;
    }

    private static int SkipHexString(byte[] content, int index)
    {
        index++;

        while (index < content.Length && content[index] != (byte)'>')
        {
            index++;
        }

        return index + 1;
    }

    /// <summary>
    /// Dados de stream são binários e podem conter qualquer byte, inclusive sequências que
    /// pareçam delimitadores. Contá-los produziria profundidade inventada.
    /// </summary>
    private static int SkipStream(byte[] content, int index)
    {
        index += "stream".Length;

        while (index < content.Length)
        {
            if (content[index] == (byte)'e' && StartsWith(content, index, "endstream"))
            {
                return index + "endstream".Length;
            }

            index++;
        }

        return index;
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
