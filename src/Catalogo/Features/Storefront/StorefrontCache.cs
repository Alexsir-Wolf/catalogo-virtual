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
    /// As chaves de query que fazem parte da identidade de uma página da vitrine: busca, filtro de
    /// categoria e página (RN-56). São **elas**, e não toda a query.
    ///
    /// Variar por toda a query era o padrão e parecia inofensivo, mas cada `?utm_source=whatsapp`,
    /// cada `?fbclid=…` e cada variante de link divulgado criava entrada própria — e com o teto de
    /// memória do cache, algumas milhares de variantes despejam justamente a entrada quente da raiz,
    /// que é o alvo da meta de tempo de resposta. Listar as três também torna a linha significativa:
    /// removê-la passa a mudar comportamento, o que antes não acontecia.
    /// </summary>
    public static readonly string[] NavigationQueryKeys = ["busca", "categoria", "pagina"];

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

                // A chave inclui as três chaves de navegação — e só elas. Busca, categoria e página
                // são estado de navegação na URL (RN-56), e cada combinação é uma página diferente;
                // parâmetro de rastreamento não é.
                .SetVaryByQuery(NavigationQueryKeys)));

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
    ///
    /// O método precisa ser de leitura, e isso importa por causa do segundo uso deste predicado:
    /// ele também decide de quais respostas o `Set-Cookie` é removido. O cache de saída já
    /// ignoraria um `POST` por conta própria, mas a remoção do cookie não — e tirar `Set-Cookie`
    /// de uma resposta a `POST` num caminho público descartaria em silêncio qualquer cookie que
    /// um fluxo futuro emitisse ali.
    /// </summary>
    public static bool IsCacheable(HttpContext context) =>
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && IsStorefront(context.Request.Path)
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
    ///
    /// **A remoção acontece no primeiro byte escrito, e não em `Response.OnStarting`.** Isso não é
    /// detalhe de implementação: o cache de saída tira o retrato dos cabeçalhos dentro do shim de
    /// stream que ele instala, no momento em que a aplicação começa a escrever o corpo — e os
    /// `OnStarting` do Kestrel só rodam depois disso. Com a remoção ali, a resposta **servida** saía
    /// limpa e a entrada **guardada** conservava o cookie do primeiro visitante, replicado em todo
    /// acerto de cache. Este middleware roda depois de `UseOutputCache`, então o stream que ele
    /// embrulha é o shim: remover o cabeçalho antes de repassar a escrita coloca a remoção **antes**
    /// do retrato. Verificado por teste, que afirma a ausência do cabeçalho nas duas respostas.
    /// </summary>
    public static IApplicationBuilder UseStorefrontCacheableResponses(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!IsCacheable(context))
            {
                await next();

                return;
            }

            var original = context.Response.Body;
            context.Response.Body = new CookieStrippingStream(context.Response, original);

            try
            {
                await next();
            }
            finally
            {
                context.Response.Body = original;
            }
        });

    /// <summary>
    /// Remove o `Set-Cookie` no primeiro byte escrito e repassa tudo adiante.
    ///
    /// Existe por causa da ordem: é o único ponto que roda **antes** de o cache de saída copiar os
    /// cabeçalhos e **depois** de o antiforgery tê-los escrito.
    /// </summary>
    private sealed class CookieStrippingStream(HttpResponse response, Stream inner) : Stream
    {
        private bool stripped;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            Strip();
            inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Strip();

            return inner.FlushAsync(cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Strip();
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Strip();
            inner.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            Strip();

            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Strip();

            return inner.WriteAsync(buffer, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private void Strip()
        {
            if (stripped)
            {
                return;
            }

            stripped = true;

            if (!response.HasStarted)
            {
                response.Headers.Remove(HeaderNames.SetCookie);
            }
        }
    }
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
