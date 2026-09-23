using Catalogo;
using Catalogo.Components;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// A plataforma termina o TLS no proxy e encaminha a requisição em HTTP (ADR-018).
// Sem isso a aplicação se enxerga como insegura e entra em loop de redirecionamento.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/health", async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var configured = configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(configured))
    {
        return Results.Problem("Connection string 'Default' is not configured.", statusCode: 503);
    }

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
});

app.Run();
