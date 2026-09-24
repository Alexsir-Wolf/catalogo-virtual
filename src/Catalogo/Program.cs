using Catalogo;
using Catalogo.Components;
using Catalogo.Data;
using Catalogo.Features.Account;
using Catalogo.Features.Categories;
using Catalogo.Features.Media;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
builder.Services.AddSingleton<ImageProcessor>();
builder.Services.AddHttpClient<IObjectStorage, SupabaseObjectStorage>();
builder.Services.AddScoped<ProductPhotoService>();
builder.Services.AddScoped<CategoryMaintenance>();

builder.Services.AddPanelAuthentication(builder.Configuration);
builder.Services.AddCascadingAuthenticationState();

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

app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();
app.UsePanelAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/health", async (
    IConfiguration configuration,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var configured = configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(configured))
    {
        return Results.Problem("Connection string 'Default' is not configured.", statusCode: 503);
    }

    try
    {
        await using var connection = new NpgsqlConnection(DatabaseConnectionString.Normalize(configured));
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("select 1", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        return Results.Ok(new
        {
            status = "healthy",
            database = connection.PostgreSqlVersion.ToString(),
            query = result
        });
    }
    catch (Exception exception)
    {
        // A mensagem da exceção carrega host e usuário do banco e por isso fica só no log.
        // A resposta devolve o código SQLSTATE, que identifica a causa sem expor nada.
        loggerFactory.CreateLogger("Health").LogError(exception, "Falha ao conectar no banco.");

        return Results.Json(new
        {
            status = "unhealthy",
            failure = exception.GetType().Name,
            cause = exception.InnerException?.GetType().Name,
            sqlState = (exception as PostgresException)?.SqlState,
            detail = exception is ArgumentException or PostgresException ? exception.Message : null,
            shape = DatabaseConnectionStringShape.Describe(configured)
        }, statusCode: 503);
    }
});

app.Run();

// Exposto para o projeto de testes montar a aplicação com WebApplicationFactory.
public partial class Program;
