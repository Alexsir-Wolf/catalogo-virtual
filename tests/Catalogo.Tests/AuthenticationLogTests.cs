using System.Text.RegularExpressions;
using Catalogo.Features.Account;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catalogo.Tests;

/// <summary>
/// O log de autenticação (T-28).
///
/// O critério "autenticação, publicação, upload e geração produzem log" estava marcado como
/// atendido e a autenticação não emitia **nada** — nem sucesso, nem falha, nem bloqueio. E o
/// fallback do framework não cobria: `appsettings.json` fixa `Microsoft.AspNetCore` em `Warning`,
/// o que descarta por prefixo o que o `SignInManager` emite em `Information`.
///
/// Num sistema de **um único usuário**, sem recuperação de senha (ADR-006), este log é o único
/// rastro de acesso que existe: sem ele, "alguém entrou no painel" é pergunta sem resposta.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthenticationLogTests : IDisposable
{
    private const string OwnerUserName = "dono-log";
    private const string OwnerPassword = "Catalogo!2026";

    private readonly RecordingLoggerProvider records = new();
    private readonly WebApplicationFactory<Program> factory;

    public AuthenticationLogTests(PostgresFixture postgres) =>
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", OwnerUserName);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", OwnerPassword);

            builder.ConfigureServices(services =>
                services.AddSingleton<ILoggerProvider>(records));
        });

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task O_acesso_concedido_e_registrado_com_o_usuario()
    {
        await SignInAsync(OwnerPassword);

        var entry = records.Entries.Single(line =>
            line.Contains("concedido", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(OwnerUserName, entry);
    }

    /// <summary>
    /// Tentativa recusada sai como **aviso**, não informação: é o sinal que alguém filtrando o log
    /// por severidade precisa ver.
    /// </summary>
    [Fact]
    public async Task A_tentativa_recusada_e_registrada_como_aviso()
    {
        await SignInAsync("senha-errada");

        Assert.Contains(
            records.Warnings,
            line => line.Contains("recusada", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// **A senha nunca entra no log.** É a outra metade do critério de T-28, e a que não tem como
    /// ser corrigida depois: senha em log de plataforma é senha vazada.
    /// </summary>
    [Fact]
    public async Task A_senha_nunca_aparece_no_log()
    {
        await SignInAsync(OwnerPassword);
        await SignInAsync("senha-errada-mas-secreta");

        var everything = string.Join("\n", records.Entries);

        Assert.DoesNotContain(OwnerPassword, everything);
        Assert.DoesNotContain("senha-errada-mas-secreta", everything);
    }

    /// <summary>
    /// O log **não** distingue usuário inexistente de senha errada, pela mesma razão que a tela não
    /// distingue: ele vai para a plataforma, e um registro dizendo "este usuário não existe"
    /// entregaria metade da credencial a quem o lesse.
    /// </summary>
    [Fact]
    public async Task O_log_nao_revela_se_o_usuario_existe()
    {
        await SignInAsync("senha-errada", userName: "usuario-que-nao-existe");

        var everything = string.Join("\n", records.Entries);

        Assert.DoesNotContain("não existe", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not found", everything, StringComparison.OrdinalIgnoreCase);
    }

    private async Task SignInAsync(string password, string? userName = null)
    {
        using var client = factory.CreateDefaultClient(new Uri("https://localhost"), new CookieHandler());

        var page = await client.GetStringAsync(PanelAuthentication.LoginPath);
        var token = Regex.Match(
            page,
            """name="__RequestVerificationToken"[^>]*value="([^"]+)""").Groups[1].Value;

        using var response = await client.PostAsync(
            PanelAuthentication.LoginPath,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "acesso",
                ["Input.UserName"] = userName ?? OwnerUserName,
                ["Input.Password"] = password,
                ["__RequestVerificationToken"] = token
            }));

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Captura as mensagens formatadas de qualquer categoria. Só as mensagens: o que se afirma é o
    /// que sairia para a plataforma.
    /// </summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> entries = [];
        private readonly List<string> warnings = [];
        private readonly Lock gate = new();

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (gate)
                {
                    return [.. entries];
                }
            }
        }

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (gate)
                {
                    return [.. warnings];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Recorder(this);

        public void Dispose()
        {
        }

        private void Record(LogLevel level, string message)
        {
            lock (gate)
            {
                entries.Add(message);

                if (level >= LogLevel.Warning)
                {
                    warnings.Add(message);
                }
            }
        }

        private sealed class Recorder(RecordingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                owner.Record(logLevel, formatter(state, exception));
        }
    }
}
