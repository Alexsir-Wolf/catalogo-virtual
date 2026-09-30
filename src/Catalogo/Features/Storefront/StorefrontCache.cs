using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Net.Http.Headers;

namespace Catalogo.Features.Storefront;

/// <summary>
/// Cache de saída da vitrine (ADR-008).
///
/// O que é cacheado é o **HTML renderizado**, contagens por faceta incluídas: além da consulta,
/// elimina-se a renderização. Isso só é possível porque a vitrine não tem estado por visitante
/// (ADR-010) — qualquer conteúdo que varie por pessoa quebraria a premissa.
///
/// **A chave é a URL completa**, com busca, filtro e página, porque todo o estado de navegação
/// vive nela (RN-56). Duas URLs diferentes são duas entradas.
///
/// **O risco desta parte do sistema é o mais insidioso do projeto:** cache que não invalida não
/// quebra nada visivelmente — só mostra dado velho, e ninguém percebe até o cliente reclamar.
/// É por isso que a invalidação usa **uma tag só** para a vitrine inteira, e não uma por rota:
/// com uma tag, esquecer de invalidar é impossível; com várias, esquecer é questão de tempo. O
/// custo é recompor páginas que talvez não precisassem — irrelevante num catálogo escrito
/// poucas vezes por semana, e muito menor que servir preço errado.
/// </summary>
public static class StorefrontCache
{
    /// <summary>
    /// A política aplicada às rotas públicas.
    /// </summary>
    public const string PolicyName = "vitrine";

    /// <summary>
    /// A tag única da vitrine. Toda escrita no painel invalida esta tag — produto, categoria,
    /// publicação, ordenação, exclusão e contato.
    /// </summary>
    public const string Tag = "vitrine";

    /// <summary>
    /// Janela de validade. Existe como rede de segurança, não como mecanismo principal: a
    /// invalidação por escrita é que mantém a vitrine correta. Se alguma escrita futura
    /// esquecer de invalidar, o dado velho tem prazo curto em vez de eterno.
    /// </summary>
    public static readonly TimeSpan Expiration = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddStorefrontCache(this IServiceCollection services) =>
        services.AddOutputCache(options =>
            // Política **base com predicado**, e não atributo por endpoint: página Razor não
            // carrega metadado de cache, e aplicar ao mapeamento inteiro pegaria o painel junto.
            // O predicado deixa explícito, em um lugar só, quais rotas são cacheáveis.
            options.AddBasePolicy(policy => policy
                .With(context => IsCacheable(context.HttpContext))
                .Tag(Tag)
                .Expire(Expiration)

                // A chave inclui a query inteira: busca, categoria e página são estado de
                // navegação na URL, e cada combinação é uma página diferente.
                .SetVaryByQuery("*")));

    /// <summary>
    /// As rotas públicas: a listagem e o detalhe do produto. O painel **nunca** é cacheado — ele
    /// mostra rascunho, situação e contagem que mudam a cada escrita, e servir isso de cache
    /// faria o dono editar contra um retrato do passado.
    /// </summary>
    public static bool IsStorefront(PathString path) =>
        path == "/" || path.StartsWithSegments("/produto", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Só requisição **anônima** é cacheável.
    ///
    /// A razão é concreta e foi descoberta medindo: o cache de saída **não armazena resposta que
    /// traga `Set-Cookie`**, e uma requisição autenticada carrega a renovação do cookie de sessão.
    /// Cachear essa resposta descartaria a renovação; não cachear mantém a sessão do dono intacta
    /// e não muda nada para o visitante, que é quem gera a carga.
    /// </summary>
    public static bool IsCacheable(HttpContext context) =>
        IsStorefront(context.Request.Path)
        && context.User.Identity?.IsAuthenticated != true;

    /// <summary>
    /// Remove o cookie de antiforgery das respostas públicas anônimas.
    ///
    /// **Sem isso o cache não guardaria nada, e em silêncio.** O Blazor exige o middleware de
    /// antiforgery no pipeline para renderizar, e ele emite `Set-Cookie` até na vitrine — o que
    /// impede o armazenamento da resposta. A vitrine é anônima e seu único formulário é de busca
    /// por GET, que não muta estado: o token não tem o que proteger ali.
    ///
    /// Medido antes e depois: com o cookie, a segunda requisição à mesma URL era recomposta do
    /// banco; sem ele, é servida do cache.
    /// </summary>
    public static IApplicationBuilder UseStorefrontCacheableResponses(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (IsCacheable(context))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.Remove(HeaderNames.SetCookie);

                    return Task.CompletedTask;
                });
            }

            await next();
        });
}

/// <summary>
/// Invalida o cache da vitrine depois de uma escrita no painel.
///
/// É **o único acoplamento** entre o painel e a vitrine, e está nomeado de propósito: um
/// `IOutputCacheStore` injetado solto em cada serviço de escrita esconderia essa dependência
/// dentro de detalhes de infraestrutura.
/// </summary>
public sealed class StorefrontInvalidation(IOutputCacheStore store, ILogger<StorefrontInvalidation> logger)
{
    public async Task InvalidateAsync(string reason, CancellationToken cancellationToken = default)
    {
        await store.EvictByTagAsync(StorefrontCache.Tag, cancellationToken);

        // O log existe porque a invalidação é invisível quando funciona e invisível quando
        // falha. Sem rastro, investigar "a vitrine está mostrando preço velho" começa do zero.
        logger.LogInformation("Cache da vitrine invalidado. Motivo: {Motivo}.", reason);
    }
}
