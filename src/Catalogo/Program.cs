using Catalogo;
using Catalogo.Components;
using Catalogo.Data;
using Catalogo.Features.Account;
using Catalogo.Features.CatalogBuilder;
using Catalogo.Features.Categories;
using Catalogo.Features.Media;
using Catalogo.Features.PdfExport;
using Catalogo.Features.Products;
using Catalogo.Features.Settings;
using Catalogo.Features.Storefront;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

// A licença Community do QuestPDF vale para uso interno e receita abaixo do teto da licença;
// sem esta linha a biblioteca lança na primeira composição (ADR-012).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Fábrica em vez de instância direta: no painel interativo o componente vive enquanto o
// circuito durar, e um DbContext de vida longa acumularia estado rastreado entre telas.
// Cada operação abre e fecha o seu (ADR-010).
builder.Services.AddDbContextFactory<CatalogDbContext>(options =>
    options.UseNpgsql(DatabaseConnectionString.Normalize(
        builder.Configuration.GetConnectionString("Default") ?? string.Empty)));

// O Identity resolve o contexto por requisição, e é a fábrica que o produz.
builder.Services.AddScoped(services =>
    services.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContext());

builder.Services.Configure<ObjectStorageOptions>(
    builder.Configuration.GetSection(ObjectStorageOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ImageProcessor>();
builder.Services.AddHttpClient<IObjectStorage, SupabaseObjectStorage>();
builder.Services.AddScoped<ProductPhotoService>();
builder.Services.AddScoped<CatalogMaintenance>();
builder.Services.AddScoped<CatalogResolution>();
builder.Services.AddScoped<CatalogComposer>();
builder.Services.AddScoped<ICoverSource, StoredCoverSource>();
builder.Services.AddScoped<CatalogGeneration>();
builder.Services.AddScoped<CategoryMaintenance>();
builder.Services.AddScoped<ProductMaintenance>();
builder.Services.AddScoped<ProductPhotoUpload>();
builder.Services.AddScoped<ProductRemoval>();
builder.Services.AddScoped<ProductPublication>();
builder.Services.AddScoped<ProductOrdering>();
builder.Services.AddScoped<ProductListing>();
builder.Services.AddScoped<StorefrontQuery>();
builder.Services.AddSingleton<PasswordAttemptLimiter>();
builder.Services.AddScoped<PortalSettingsService>();

builder.Services.AddStorefrontCache();
builder.Services.AddScoped<StorefrontInvalidation>();

builder.Services.AddPanelAuthentication(builder.Configuration);
builder.Services.AddCascadingAuthenticationState();

// O gate do painel roda na requisição HTTP inicial e não vê as interações do circuito. Sem
// este provedor, uma aba aberta segue operando depois de a senha mudar ou a sessão ser
// encerrada (R-09 de REVIEW-T-31-2026-09-29).
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingAuthenticationState>();

// A plataforma termina o TLS no proxy e encaminha a requisição em HTTP (ADR-018).
// Sem isso a aplicação se enxerga como insegura e entra em loop de redirecionamento.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

await app.ApplyPendingMigrationsAsync();
await app.SeedOwnerAccountAsync();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UsePanelAuthorization();

// Depois da autenticação: é o que permite ao antiforgery vincular o token à identidade
// do usuário. Antes dela, `HttpContext.User` ainda é anônimo e a vinculação não ocorre.
//
app.UseAntiforgery();

// Depois do antiforgery e antes do mapeamento: o cache de saída serve as rotas públicas da
// vitrine (ADR-008). O painel não passa por aqui — a política é aplicada por rota.
app.UseOutputCache();

// Depois do cache e antes do mapeamento: limpa o `Set-Cookie` de antiforgery das respostas
// públicas anônimas, sem o qual o cache acima não guardaria nada.
app.UseStorefrontCacheableResponses();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// A saúde do serviço, para quem monitora e para a plataforma (T-28).
//
// **É público**: quem monitora não tem credencial do painel. Por isso a resposta é pobre de
// propósito — rótulos de estado e tipos de falha, nunca host, usuário, chave ou versão.
//
// **Cada verificação tem teto de tempo próprio** (`ProbeTimeout`, 5s), e não por economia: esta
// rota é o `healthCheckPath` do `render.yaml`, e uma sonda pendurada não responde "não saudável" —
// não responde nada, e a plataforma trata a instância como travada e reinicia. O estouro do teto
// volta como resultado não saudável, nunca como exceção.
//
// As duas verificações vivem em peças próprias (`DatabaseHealth`, `StorageHealth`) porque o ramo
// que interessa proteger é o de falha, e ele não é alcançável de dentro do `MapGet` num teste: com
// um banco inalcançável a aplicação nem sobe, já que aplica migrações na partida. O corpo do 503
// em si é exercitado por HTTP, trocando a cadeia da sonda depois da partida.
app.MapGet("/health", async (
    IConfiguration configuration,
    IObjectStorage objectStorage,
    IOptions<ObjectStorageOptions> storageOptions,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("Health");

    var database = await DatabaseHealth.CheckAsync(
        configuration.GetConnectionString("Default"),
        logger,
        cancellationToken);

    if (!database.Healthy)
    {
        // Sem banco não há vitrine nem painel: aqui o 503 é o estado verdadeiro do serviço.
        //
        // **E é só isso que o corpo diz.** O tipo da falha, o tipo da causa e o `SqlState` ficaram
        // no `LogError` de `DatabaseHealth`, onde já estão como campos próprios: publicados aqui,
        // eles distinguiam para um anônimo "não resolvi o host" de "senha recusada" (`28P01`) de
        // "banco não existe" (`3D000`) — nenhum é segredo isolado, e juntos são a topologia interna
        // que R-01 de REVIEW-T-02 pediu para não publicar. Quem monitora reage ao `unhealthy`; quem
        // investiga tem o log (R-02 de REVIEW-T-28-2026-09-30).
        return Results.Json(new { status = "unhealthy" }, statusCode: 503);
    }

    // O armazenamento de objeto é a segunda dependência externa (ADR-018), e uma falha nele é
    // invisível no banco: as páginas respondem e as imagens não abrem. Verificar só o banco daria
    // "healthy" com a vitrine quebrada.
    var storage = await StorageHealth.CheckAsync(
        objectStorage,
        storageOptions.Value,
        logger,
        cancellationToken,
        app.Environment.IsDevelopment());

    // **O armazenamento degradado não derruba o endpoint, e isso é decisão, não descuido.**
    //
    // Esta rota é o `healthCheckPath` do serviço (`render.yaml`), então um 503 aqui não é um aviso:
    // é a plataforma tirando a instância de serviço e barrando o próximo deploy. Com o Supabase
    // Storage fora do ar, o banco responde, a vitrine renderiza e só as fotos quebram — devolver
    // 503 nesse estado trocaria fotos quebradas por site fora do ar, cada reinício custando um
    // minuto de partida a frio (ADR-018). É o mesmo princípio que T-07 fixou: falha de uma parte
    // não tira a vitrine pública do ar.
    //
    // Quem monitora continua vendo o problema — `status` diz `degraded` — e o log traz o detalhe.
    // O que muda é quem decide o que fazer: uma pessoa, não o orquestrador.
    if (!storage.Healthy)
    {
        logger.LogWarning(
            "Armazenamento degradado: {Detalhe}. A resposta segue 200 porque esta rota é o health "
            + "check da plataforma e o banco está respondendo.",
            storage.Detail);
    }

    // O campo `storage` leva o **rótulo**, não o detalhe: `not configured` contava a um anônimo
    // que a credencial do armazenamento não foi preenchida no painel, e o tipo da exceção contava
    // por que o acesso falhou. É o mesmo critério do 503 acima — o detalhe é do log.
    return Results.Json(
        new
        {
            status = storage.Healthy ? "healthy" : "degraded",
            database = "reachable",
            storage = storage.Label
        },
        statusCode: 200);
});

app.Run();

// Exposto para o projeto de testes montar a aplicação com WebApplicationFactory.
public partial class Program;
