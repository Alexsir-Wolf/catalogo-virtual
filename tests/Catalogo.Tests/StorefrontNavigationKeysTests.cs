using System.Reflection;
using Catalogo.Features.Storefront;
using Microsoft.AspNetCore.Components;

namespace Catalogo.Tests;

/// <summary>
/// A chave do cache da vitrine cobre **todos** os parâmetros de navegação da página (T-21, ADR-008).
///
/// `StorefrontCache.NavigationQueryKeys` alimenta o `SetVaryByQuery`: o que não está nessa lista não
/// entra na chave, e duas URLs que mostram conteúdo diferente passam a dividir a mesma entrada — o
/// visitante recebe a página do anterior, sem exceção, sem log e sem nada quebrar. É o defeito mais
/// silencioso do projeto, e o caminho até ele é banal: alguém acrescenta um `[SupplyParameterFromQuery]`
/// à vitrine e não lembra que existe uma política de cache do outro lado.
///
/// A lista já **referencia** as constantes da página, o que fecha o rename; o que este caso fecha é a
/// adição. Não é hipótese remota: a ADR-008 lista a ordenação entre os componentes da chave e a vitrine
/// ainda não a tem — o próximo parâmetro é justamente o previsto pela arquitetura.
///
/// Por isso a afirmação é sobre o conjunto inteiro, lido por reflexão da própria página: no dia da
/// adição o caso falha, e a mensagem diz qual parâmetro ficou de fora.
/// </summary>
public sealed class StorefrontNavigationKeysTests
{
    [Fact]
    public void A_chave_do_cache_cobre_todos_os_parametros_de_navegacao_da_vitrine()
    {
        var declaredOnPage = QueryParametersOf(typeof(Storefront)).ToHashSet();
        var coveredByCache = StorefrontCache.NavigationQueryKeys.ToHashSet();

        Assert.True(
            declaredOnPage.SetEquals(coveredByCache),
            $"Parâmetros da vitrine fora da chave do cache: {Describe(declaredOnPage.Except(coveredByCache))}. "
            + $"Chaves do cache que a vitrine não tem mais: {Describe(coveredByCache.Except(declaredOnPage))}. "
            + "Acrescente o parâmetro a StorefrontCache.NavigationQueryKeys, ou duas URLs diferentes "
            + "vão compartilhar a mesma entrada de cache.");
    }

    private static string Describe(IEnumerable<string> keys) =>
        string.Join(", ", keys.Order().DefaultIfEmpty("nenhum"));

    /// <summary>
    /// O nome que chega na URL é o do atributo; sem `Name`, o Blazor usa o nome da propriedade — e é
    /// esse nome, não o do identificador em C#, que precisa constar da chave do cache.
    /// </summary>
    private static IEnumerable<string> QueryParametersOf(Type page) =>
        page.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => new
            {
                property.Name,
                Query = property.GetCustomAttribute<SupplyParameterFromQueryAttribute>()
            })
            .Where(candidate => candidate.Query is not null)
            .Select(candidate => candidate.Query!.Name ?? candidate.Name);
}
