using System.Net;
using System.Text.RegularExpressions;
using Catalogo.Data;
using Catalogo.Features.Account;
using Catalogo.Features.Media;
using Catalogo.Features.Settings;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using PdfSharp;
using PdfSharp.Pdf;

namespace Catalogo.Tests;

/// <summary>
/// Configurações do portal (T-31). Banco próprio por caso: o registro é único por
/// definição, e trocar a senha do dono num banco compartilhado quebraria a autenticação
/// das outras classes de teste.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PortalSettingsTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private const string SettingsPath = "/painel/configuracoes";

    private const string OwnerUserName = "dono-config";
    private const string OwnerPassword = "Catalogo!2026";
    private const string NewPassword = "Catalogo!2027";

    private IsolatedDatabase? database;
    private string connectionString = string.Empty;
    private WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        database = await IsolatedDatabase.CreateAsync(postgres, "config");
        connectionString = database.ConnectionString;

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:UserName", OwnerUserName);
            builder.UseSetting($"{OwnerAccountOptions.SectionName}:Password", OwnerPassword);
        });
    }

    public async Task DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }

    public void Dispose() => factory?.Dispose();

    [Fact]
    public async Task A_tela_de_configuracoes_exige_autenticacao()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/painel/configuracoes");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>
    /// RN-65: sem capa a geração está bloqueada, e a UI-10 é a única pista disso. O aviso
    /// não é decorativo — sem ele o dono descobre em UI-08, sem saber por quê.
    /// </summary>
    [Fact]
    public async Task UI_10_semCapa_avisa_que_a_geracao_esta_bloqueada()
    {
        using var client = await SignedInClientAsync();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/painel/configuracoes"));

        // A asserção é sobre o **elemento do aviso**, não sobre a página: a folha também
        // emite `data-estado="semCapa"` pelo estado geral da tela, então apagar o parágrafo
        // da RN-65 deixaria uma busca no documento inteiro passar (R-13 de
        // `REVIEW-T-31-2026-09-29`).
        Assert.Matches(
            """<p class="campo__erro"[^>]*data-estado="semCapa"[^>]*>""",
            html);
        Assert.Contains("geração de catálogos em PDF está", html);
    }

    /// <summary>
    /// O caminho completo passa pelo armazenamento real, como os testes de T-08: gravar a
    /// capa é metade do critério, e simular o armazenamento provaria a outra metade só.
    /// </summary>
    [SkippableFact]
    public async Task CA_34_pdf_de_uma_pagina_em_retrato_vira_a_capa()
    {
        var service = ServiceOrSkip();

        var inspection = await service.SaveCoverAsync(Pdf(PageSize.A4, pages: 1));

        Assert.True(inspection.Accepted);
        Assert.True((await service.LoadAsync()).HasCover);
    }

    /// <summary>
    /// CA-35 no caminho completo: a recusa acontece **antes** de qualquer escrita, então o
    /// registro não é tocado e o armazenamento não recebe nada.
    /// </summary>
    [Fact]
    public async Task CA_35_pdf_com_tres_paginas_e_recusado_e_nada_e_gravado()
    {
        var service = Service();

        var inspection = await service.SaveCoverAsync(Pdf(PageSize.A4, pages: 3));

        Assert.Equal(CoverRejection.PageCount, inspection.Rejection);
        Assert.Equal(3, inspection.PagesFound);
        Assert.False((await service.LoadAsync()).HasCover);
    }

    [Fact]
    public async Task Arquivo_que_nao_e_pdf_e_recusado_no_envio()
    {
        var service = Service();

        var inspection = await service.SaveCoverAsync([1, 2, 3, 4, 5]);

        Assert.Equal(CoverRejection.NotAPdf, inspection.Rejection);
        Assert.False((await service.LoadAsync()).HasCover);
    }

    /// <summary>
    /// O critério mais fácil de violar sem perceber: um envio recusado não pode levar a
    /// capa que já estava lá. A validação vem antes da escrita justamente por isso.
    /// </summary>
    [SkippableFact]
    public async Task Envio_recusado_preserva_a_capa_anterior()
    {
        var service = ServiceOrSkip();

        await service.SaveCoverAsync(Pdf(PageSize.A4, pages: 1));
        var aceita = (await service.LoadAsync()).CoverFileName;

        await service.SaveCoverAsync(Pdf(PageSize.A4, pages: 5));

        Assert.Equal(aceita, (await service.LoadAsync()).CoverFileName);
    }

    /// <summary>
    /// A mesma garantia do caso acima, **sem depender de credencial**: a recusa acontece
    /// antes de qualquer upload, então dá para plantar a capa direto no registro e
    /// verificar que um arquivo inválido não a leva. É o critério que mais importa dos
    /// dez, e ele não pode ficar refém do ambiente.
    /// </summary>
    [Fact]
    public async Task Envio_recusado_preserva_a_capa_anterior_sem_tocar_o_armazenamento()
    {
        const string anterior = "capa/plantada-pelo-teste.pdf";

        // Plantar direto no registro exige o esquema no lugar, e este caso escreve antes
        // de qualquer requisição — que é quem normalmente dispara a migration na subida.
        await database!.MigrateAsync();

        await using (var context = CreateContext())
        {
            // A linha já existe: é semeada pela migration desde a correção de R-04 de
            // `REVIEW-T-31-2026-09-29`. Plantar a capa anterior é atualizar, não inserir.
            await context.PortalSettings
                .Where(entity => entity.Id == PortalSettings.SingletonId)
                .ExecuteUpdateAsync(update =>
                    update.SetProperty(entity => entity.CoverFileName, anterior));
        }

        var inspection = await Service().SaveCoverAsync(Pdf(PageSize.A4, pages: 4));

        Assert.Equal(CoverRejection.PageCount, inspection.Rejection);
        Assert.Equal(anterior, (await Service().LoadAsync()).CoverFileName);
    }

    /// <summary>
    /// Nome novo a cada envio, como nas derivadas de foto (RN-13): substituir no mesmo
    /// nome deixaria cache de borda servindo a capa antiga.
    /// </summary>
    [SkippableFact]
    public async Task Substituir_a_capa_gera_nome_novo()
    {
        var service = ServiceOrSkip();

        await service.SaveCoverAsync(Pdf(PageSize.A4, pages: 1));
        var primeira = (await service.LoadAsync()).CoverFileName;

        await service.SaveCoverAsync(Pdf(PageSize.A4, pages: 1));
        var segunda = (await service.LoadAsync()).CoverFileName;

        Assert.NotNull(primeira);
        Assert.NotEqual(primeira, segunda);
    }

    /// <summary>
    /// CA-38: alterar o contato reflete na vitrine imediatamente. A vitrine lê do banco a
    /// cada requisição, então "imediatamente" é verificável sem esperar nada — e é o que
    /// a RN-68 exige.
    /// </summary>
    [Fact]
    public async Task CA_38_alterar_o_contato_reflete_na_vitrine_imediatamente()
    {
        using var client = factory.CreateClient();

        var antes = await client.GetStringAsync("/");
        Assert.Contains("""data-estado="semContato" """.TrimEnd(), antes);

        await Service().SaveContactAsync(new ContactDraft
        {
            WhatsApp = "5588996541931",
            Phone = "(88) 99654-1931",
            Email = "vendas@exemplo.com.br"
        });

        var depois = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        Assert.Contains("""data-estado="comContato" """.TrimEnd(), depois);
        Assert.Contains("https://wa.me/5588996541931", depois);
        Assert.Contains("(88) 99654-1931", depois);
        Assert.Contains("vendas@exemplo.com.br", depois);
    }

    [Fact]
    public async Task Canal_nao_configurado_nao_aparece_na_vitrine()
    {
        await Service().SaveContactAsync(new ContactDraft { Phone = "(88) 99654-1931" });

        using var client = factory.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        Assert.Contains("(88) 99654-1931", html);
        Assert.DoesNotContain("wa.me", html);
        Assert.DoesNotContain("mailto:", html);
    }

    /// <summary>
    /// R-04 de `REVIEW-T-31-2026-09-29`: a leitura da configuração acontece no layout da
    /// vitrine, que é público e anônimo. Quando ela **inseria** a linha ausente, duas visitas
    /// simultâneas a um banco recém implantado disputavam a chave fixa e uma recebia violação
    /// de unicidade — 500 na página pública. A linha agora é semeada na migration, e a
    /// leitura não escreve.
    /// </summary>
    [Fact]
    public async Task RN_61_a_migration_semeia_o_registro_unico()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var rows = await context.PortalSettings.CountAsync();

        Assert.Equal(1, rows);
    }

    /// <summary>
    /// A contraprova do caso acima: apagada a linha, a leitura devolve configuração vazia e
    /// **não a recria**. Sem isso, a correção passaria despercebida se alguém restaurasse a
    /// inserção no caminho de leitura.
    /// </summary>
    [Fact]
    public async Task Leitura_sem_registro_nao_grava_nada()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await context.PortalSettings.ExecuteDeleteAsync();

        var settings = await Service().LoadAsync();

        Assert.False(settings.HasContact);
        Assert.False(settings.HasCover);
        Assert.Equal(0, await context.PortalSettings.CountAsync());
    }

    /// <summary>
    /// A ponta de integração de R-07: recusa **não grava nada**. Um canal inválido não pode
    /// derrubar os outros dois que já estavam certos.
    /// </summary>
    [Fact]
    public async Task RN_67_contato_recusado_nao_altera_o_que_estava_gravado()
    {
        var service = Service();
        await service.SaveContactAsync(new ContactDraft { Phone = "(88) 99654-1931" });

        var outcome = await service.SaveContactAsync(new ContactDraft
        {
            WhatsApp = "99654-1931",
            Phone = "(11) 3333-4444"
        });

        Assert.Equal(ContactField.WhatsApp, outcome.Rejected);
        Assert.Equal("(88) 99654-1931", (await service.LoadAsync()).Phone);
    }

    /// <summary>
    /// O número chega ao banco só com dígitos, que é o que a URL de conversa exige — a
    /// vitrine não tem como consertar depois.
    /// </summary>
    [Fact]
    public async Task RN_67_whatsapp_e_gravado_apenas_com_digitos()
    {
        var service = Service();

        await service.SaveContactAsync(new ContactDraft { WhatsApp = "+55 (88) 99654-1931" });

        Assert.Equal("5588996541931", (await service.LoadAsync()).WhatsApp);
    }

    [Fact]
    public async Task Contato_em_branco_e_gravado_como_ausencia()
    {
        var service = Service();

        await service.SaveContactAsync(new ContactDraft { WhatsApp = "   ", Email = "" });

        var settings = await service.LoadAsync();

        Assert.Null(settings.WhatsApp);
        Assert.Null(settings.Email);
        Assert.False(settings.HasContact);
    }

    /// <summary>
    /// CA-39: trocar a senha exige a atual, e a anterior **deixa de dar acesso**. A
    /// segunda metade é a que importa — trocar sem invalidar a anterior não é trocar.
    /// </summary>
    [Fact]
    public async Task CA_39_trocar_a_senha_invalida_a_anterior()
    {
        using var _ = await SignedInClientAsync();

        var outcome = await Service().ChangePasswordAsync(
            OwnerUserName,
            OwnerPassword,
            NewPassword);

        Assert.True(outcome.Succeeded);
        Assert.False(await CanSignInAsync(OwnerPassword));
        Assert.True(await CanSignInAsync(NewPassword));
    }

    /// <summary>
    /// R-03 de `REVIEW-T-31-2026-09-29`: trocar a senha precisa cortar **a sessão já
    /// aberta**, e não apenas a senha. Era o que faltava — o selo de segurança era renovado
    /// e ninguém o conferia, então o cookie emitido antes seguia abrindo o painel. É o
    /// cenário que motiva trocar a senha: alguém ficou com uma sessão de pé.
    ///
    /// A verificação é feita reusando o **mesmo cliente** que autenticou antes da troca. O
    /// caso anterior prova a senha; este prova o acesso.
    /// </summary>
    [Fact]
    public async Task CA_39_a_sessao_aberta_antes_da_troca_perde_o_acesso()
    {
        using var client = await SignedInClientAsync();

        using var before = await client.GetAsync(SettingsPath);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await Service().ChangePasswordAsync(OwnerUserName, OwnerPassword, NewPassword);

        using var after = await client.GetAsync(SettingsPath);

        // O cookie deixou de valer: a requisição cai na tela de acesso em vez de abrir a
        // tela de configurações.
        Assert.Contains(
            PanelAuthentication.LoginPath,
            after.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RN_66_senha_atual_incorreta_nao_altera_nada()
    {
        using var _ = await SignedInClientAsync();

        var outcome = await Service().ChangePasswordAsync(
            OwnerUserName,
            "SenhaQueNaoE!2026",
            NewPassword);

        Assert.Equal(PasswordFailure.CurrentPasswordWrong, outcome.Failure);
        Assert.True(await CanSignInAsync(OwnerPassword));
        Assert.False(await CanSignInAsync(NewPassword));
    }

    /// <summary>
    /// RN-60 na troca de senha: a confirmação da senha atual passa pelo `UserManager`, fora
    /// do `SignInManager`, então o bloqueio da tela de acesso não a alcançava — o formulário
    /// era um caminho de tentativa ilimitada para quem já tinha sessão aberta (R-12 de
    /// `REVIEW-T-31-2026-09-29`).
    /// </summary>
    [Fact]
    public async Task RN_60_tentativas_sucessivas_na_senha_atual_bloqueiam_a_troca()
    {
        var service = Service();

        for (var attempt = 0; attempt < PanelAuthentication.MaxFailedAccessAttempts; attempt++)
        {
            await service.ChangePasswordAsync(OwnerUserName, "SenhaQueNaoE!2026", NewPassword);
        }

        // Agora **com a senha certa**: o bloqueio é por tentativas, e precisa valer mesmo
        // para quem acertou — senão bastaria errar quatro vezes e acertar na quinta.
        var outcome = await service.ChangePasswordAsync(
            OwnerUserName,
            OwnerPassword,
            NewPassword);

        Assert.Equal(PasswordFailure.CurrentPasswordWrong, outcome.Failure);
        Assert.Contains("tentativas", outcome.Message!);
        Assert.True(await CanSignInAsync(OwnerPassword));
    }

    /// <summary>
    /// A contraprova: acertar a senha atual zera a contagem, senão erros espalhados ao longo
    /// do tempo acabariam bloqueando o dono legítimo sem nenhuma tentativa de invasão.
    /// </summary>
    [Fact]
    public async Task Troca_bem_sucedida_zera_a_contagem_de_tentativas()
    {
        var service = Service();

        await service.ChangePasswordAsync(OwnerUserName, "SenhaQueNaoE!2026", NewPassword);
        await service.ChangePasswordAsync(OwnerUserName, OwnerPassword, NewPassword);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OwnerAccount>>();
        var owner = await users.FindByNameAsync(OwnerUserName);

        Assert.Equal(0, await users.GetAccessFailedCountAsync(owner!));
    }

    /// <summary>
    /// Senha nova fora da política do Identity é recusada com o motivo, e no campo certo —
    /// não pode ser confundida com senha atual errada (UI-10.senhaIncorreta).
    /// </summary>
    [Fact]
    public async Task Senha_nova_fora_da_politica_e_recusada_sem_alterar()
    {
        using var _ = await SignedInClientAsync();

        var outcome = await Service().ChangePasswordAsync(OwnerUserName, OwnerPassword, "abc");

        Assert.Equal(PasswordFailure.NewPasswordRejected, outcome.Failure);
        Assert.NotEmpty(outcome.Message!);
        Assert.True(await CanSignInAsync(OwnerPassword));
    }

    /// <summary>
    /// "Dar acesso" é verificado no Identity, e não por um POST no formulário: a resposta
    /// do formulário é 200 tanto no acerto quanto na recusa, e o bloqueio por tentativas
    /// da RN-60 contamina a segunda verificação do mesmo caso.
    /// </summary>
    private async Task<bool> CanSignInAsync(string password)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OwnerAccount>>();

        var owner = await users.FindByNameAsync(OwnerUserName);

        return owner is not null && await users.CheckPasswordAsync(owner, password);
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateDefaultClient(new Uri("https://localhost"), new CookieHandler());

        var page = await client.GetStringAsync(PanelAuthentication.LoginPath);
        var token = Regex.Match(
            page,
            """name="__RequestVerificationToken"[^>]*value="([^"]+)""").Groups[1].Value;

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "acesso",
            ["Input.UserName"] = OwnerUserName,
            ["Input.Password"] = OwnerPassword,
            ["__RequestVerificationToken"] = token
        });

        using var response = await client.PostAsync(PanelAuthentication.LoginPath, form);
        response.EnsureSuccessStatusCode();

        return client;
    }

    /// <summary>
    /// CA-34, com verificação que **roda**: um PDF de uma página em retrato é aceito e
    /// **vira a capa**. O caso que provava isso dependia de credencial do Supabase e era
    /// pulado, então remover a gravação do nome deixaria a suíte verde com o critério
    /// principal da tarefa descumprido (R-05 de `REVIEW-T-31-2026-09-29`).
    ///
    /// O armazenamento é substituído por um que registra o que recebeu — o mesmo recurso que
    /// T-13 já usava para provar persistência sem credencial. O caso do armazenamento real
    /// continua existindo, pulado, porque é ele que exerce o Supabase de verdade.
    /// </summary>
    [Fact]
    public async Task CA_34_pdf_aceito_vira_a_capa_e_vai_para_o_armazenamento()
    {
        await database!.MigrateAsync();

        var storage = new RecordingObjectStorage();
        var service = ServiceWith(storage);

        var inspection = await service.SaveCoverAsync(Pdf(PageSize.A4, pages: 1));
        var settings = await service.LoadAsync();

        Assert.True(inspection.Accepted);
        Assert.True(settings.HasCover);
        Assert.Equal(settings.CoverFileName, Assert.Single(storage.Uploaded));
        Assert.StartsWith("capa/", settings.CoverFileName);
        Assert.EndsWith(".pdf", settings.CoverFileName);
    }

    /// <summary>
    /// A contraprova do caso acima, no caminho de recusa: sem aceitação, nada é enviado ao
    /// armazenamento. Antes só era possível afirmar isso com credencial.
    /// </summary>
    [Fact]
    public async Task Capa_recusada_nao_chega_ao_armazenamento()
    {
        await database!.MigrateAsync();

        var storage = new RecordingObjectStorage();

        var inspection = await ServiceWith(storage).SaveCoverAsync(Pdf(PageSize.A4, pages: 2));

        Assert.False(inspection.Accepted);
        Assert.Empty(storage.Uploaded);
    }

    /// <summary>
    /// O serviço com o armazenamento trocado. O <c>UserManager</c> vem do contêiner da
    /// aplicação porque depende do banco e da configuração do Identity, e montá-lo à mão
    /// seria reconstruir T-07 dentro do teste.
    /// </summary>
    private PortalSettingsService ServiceWith(IObjectStorage storage)
    {
        var scope = factory.Services.CreateScope();

        return new PortalSettingsService(
            new SettingsContextFactory(connectionString),
            storage,
            Options.Create(new ObjectStorageOptions
            {
                Url = "https://armazenamento.invalido",
                ServiceKey = "chave-de-teste"
            }),
            scope.ServiceProvider.GetRequiredService<UserManager<OwnerAccount>>(),
            NullLogger<PortalSettingsService>.Instance);
    }

    private sealed class SettingsContextFactory(string connectionString)
        : IDbContextFactory<CatalogDbContext>
    {
        public CatalogDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(connectionString)
                .Options);
    }

    /// <summary>Armazenamento que aceita tudo e guarda o nome do que recebeu.</summary>
    private sealed class RecordingObjectStorage : IObjectStorage
    {
        private readonly List<string> uploaded = [];

        public IReadOnlyList<string> Uploaded => uploaded;

        public Task UploadAsync(
            string bucket,
            string objectName,
            byte[] content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            uploaded.Add(objectName);

            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            string bucket,
            string objectName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string PublicUrlFor(string objectName) => objectName;
    }

    /// <summary>
    /// O serviço é resolvido do próprio contêiner da aplicação, e não montado à mão: ele
    /// depende de armazenamento de objeto e do <c>UserManager</c>, e o teste precisa do
    /// mesmo grafo que a tela usa.
    /// </summary>
    private PortalSettingsService Service()
    {
        var scope = factory.Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<PortalSettingsService>();
    }

    /// <summary>
    /// Sem credencial do Supabase o envio da capa não tem para onde ir. Mesmo tratamento
    /// de T-08: o teste é pulado com o motivo, e não desligado nem simulado.
    /// </summary>
    private PortalSettingsService ServiceOrSkip()
    {
        var options = factory.Services.GetRequiredService<IOptions<ObjectStorageOptions>>();

        Skip.IfNot(
            options.Value.IsConfigured,
            "Defina Supabase__Url e Supabase__ServiceKey para exercer o envio real da capa.");

        return Service();
    }

    private CatalogDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);

    private static byte[] Pdf(PageSize size, int pages)
    {
        using var document = new PdfDocument();

        for (var page = 0; page < pages; page++)
        {
            document.AddPage().Size = size;
        }

        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);

        return stream.ToArray();
    }
}
