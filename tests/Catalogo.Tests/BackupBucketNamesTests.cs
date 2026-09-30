using System.Text.RegularExpressions;
using Catalogo.Features.Media;
using Xunit;

namespace Catalogo.Tests;

/// <summary>
/// Os nomes dos buckets são um contrato que o compilador não enxerga: a aplicação os traz como
/// padrão em <see cref="ObjectStorageOptions"/> e o backup os repete à mão em `ops/backup.sh`.
/// Quando os dois divergem, nada quebra na hora — a rotina copia um bucket que não existe e
/// deixa de copiar o que guarda o original e a derivada de impressão, e a perda só aparece na
/// restauração. Foi exatamente o que aconteceu: o script ficou em `produtos-originais` depois
/// que o bucket privado passou a se chamar `produtos-print`.
/// </summary>
public sealed class BackupBucketNamesTests
{
    private const string BackupScriptRelativePath = "ops/backup.sh";
    private const string SolutionFileName = "Catalogo.sln";

    [SkippableFact]
    public void Backup_copia_exatamente_os_buckets_que_a_aplicacao_escreve()
    {
        var script = ReadBackupScriptOrSkip();
        var defaults = new ObjectStorageOptions();

        var expected = new[] { defaults.PublicBucket, defaults.PrivateBucket }.Order().ToArray();

        Assert.Equal(expected, BucketsCopiedBy(script));
    }

    /// <summary>
    /// Lê a linha `readonly BUCKETS=(...)` do script. Ordenar os dois lados torna a asserção
    /// sobre o **conjunto** copiado, e não sobre a ordem em que o laço os visita.
    /// </summary>
    private static string[] BucketsCopiedBy(string script)
    {
        var declaration = Regex.Match(script, @"^readonly BUCKETS=\((?<names>[^)]*)\)", RegexOptions.Multiline);

        Assert.True(declaration.Success, $"`{BackupScriptRelativePath}` não declara mais `BUCKETS`.");

        return Regex.Matches(declaration.Groups["names"].Value, "\"(?<name>[^\"]+)\"")
            .Select(match => match.Groups["name"].Value)
            .Order()
            .ToArray();
    }

    /// <summary>
    /// O script vive no repositório, não na saída de compilação: a busca sobe da pasta do teste
    /// até a solução. Fora de um clone — uma execução a partir de artefato publicado — não há o
    /// que verificar, e pular é honesto; falhar acusaria o script de um defeito que ele não tem.
    /// </summary>
    private static string ReadBackupScriptOrSkip()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        Skip.If(directory is null, $"Sem o repositório em disco não há `{BackupScriptRelativePath}` para conferir.");

        var path = Path.Combine(directory!.FullName, BackupScriptRelativePath);

        Skip.If(!File.Exists(path), $"`{BackupScriptRelativePath}` não encontrado.");

        return File.ReadAllText(path);
    }
}
