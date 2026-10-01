using System.Security.Claims;
using Catalogo.Features.Account;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Catalogo.Tests;

/// <summary>
/// A guarda de escrita das páginas do painel, exercitada pelo comportamento e não pela anotação.
///
/// **É este o caso que faltava nos três rounds de T-31.** `PanelAuthorizationTests` confere que o
/// `[Authorize]` existe; isso prova que a anotação está lá, não que uma gravação seja recusada. O
/// round 3 mostrou o custo dessa diferença: o `AuthorizeRouteView` foi declarado como enforcement
/// do circuito, compilou, passou nos testes de anotação, e não bloqueava nada — porque vivia no
/// renderizador de SSR, fora do circuito (R-01 de `REVIEW-T-31-2026-09-30-round3`).
///
/// O que estes casos fazem: uma página falsa que herda `PanelPageBase` como as **seis** páginas que
/// gravam herdam, com um "gravar" que conta quantas vezes chegou ao fim. Com sessão viva, grava.
/// Com o estado anônimo que `ForceSignOut` instala, **não grava** e manda para a tela de acesso com
/// recarga e com o `retorno` preservado. Sem banco, sem navegador, sem Docker.
///
/// **O que eles não cobrem, para o próximo round não precisar descobrir:** a página falsa recebe
/// `Navigation` e `Authentication` por atribuição direta, então nenhum caso aqui atravessa `[Inject]`
/// nem `[CascadingParameter]`; e nenhum caso aqui observa as páginas reais chamando a guarda. Essa
/// segunda metade é de `PanelAuthorizationTests.Pagina_interativa_do_painel_deriva_da_base_com_guarda`,
/// que cobre a regra por varredura.
/// </summary>
public sealed class PanelPageGuardTests
{
    [Fact]
    public async Task Sessao_viva_deixa_a_escrita_acontecer()
    {
        var page = PageWith(Authenticated());

        await page.WriteAsync();

        Assert.Equal(1, page.Writes);
        Assert.Null(page.Navigator.LastDestination);
    }

    /// <summary>
    /// O cenário do round 2 e do round 3: o dono encerra a sessão de outro dispositivo, a
    /// revalidação troca o estado por anônimo, e quem está na aba aberta clica em salvar.
    /// </summary>
    [Fact]
    public async Task Sessao_morta_recusa_a_escrita()
    {
        var page = PageWith(Anonymous());

        await page.WriteAsync();

        Assert.Equal(0, page.Writes);
    }

    /// <summary>
    /// Recusar não basta: quem perdeu a sessão precisa sair da tela, e com **recarga**. O estado
    /// morto vive dentro do circuito atual, então navegar por dentro dele o preservaria e a
    /// interação seguinte tentaria gravar de novo.
    /// </summary>
    [Fact]
    public async Task Sessao_morta_leva_para_a_tela_de_acesso_com_recarga()
    {
        var page = PageWith(Anonymous());

        await page.WriteAsync();

        Assert.True(page.Navigator.LastForceLoad);
    }

    /// <summary>
    /// O destino carrega o `retorno`, como o gate por caminho faz: quem perde a sessão editando um
    /// produto reautentica e volta para o produto, não para a raiz do painel.
    /// </summary>
    [Fact]
    public async Task Sessao_morta_preserva_o_retorno_na_tela_de_acesso()
    {
        var page = PageWith(Anonymous());

        await page.WriteAsync();

        Assert.Equal(
            $"{PanelAuthentication.LoginPath}?{PanelAuthentication.ReturnUrlParameter}=%2Fpainel%2Fconfiguracoes",
            page.Navigator.LastDestination);
    }

    /// <summary>
    /// Estado ausente é recusa, não permissão: é a falha segura do `PanelSession`, verificada aqui
    /// pelo caminho da página — uma tela que esquecesse o `CascadingAuthenticationState` não passa
    /// a poder gravar por isso.
    /// </summary>
    [Fact]
    public async Task Estado_ausente_recusa_a_escrita()
    {
        var page = new FakePanelPage();

        await page.WriteAsync();

        Assert.Equal(0, page.Writes);
    }

    /// <summary>
    /// A recusa acontece **antes** de qualquer efeito, e esta é a diferença entre recusar e
    /// desfazer: a escrita não chega a começar, então não há estado parcial para reverter.
    /// </summary>
    [Fact]
    public async Task Recusa_acontece_antes_de_qualquer_efeito()
    {
        var page = PageWith(Anonymous());

        await page.WriteAsync();

        Assert.False(page.Started);
    }

    private static FakePanelPage PageWith(ClaimsPrincipal user) =>
        new() { State = Task.FromResult(new AuthenticationState(user)) };

    private static ClaimsPrincipal Authenticated() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "dono")], "Cookies"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    /// <summary>
    /// Herda a base pelo mesmo caminho que as páginas reais. `Started` existe para distinguir
    /// "recusou antes de começar" de "começou e abortou".
    /// </summary>
    private sealed class FakePanelPage : PanelPageBase
    {
        public FakePanelPage() => Navigation = Navigator;

        public RecordingNavigationManager Navigator { get; } = new();

        public int Writes { get; private set; }

        public bool Started { get; private set; }

        public Task<AuthenticationState>? State
        {
            get => Authentication;
            set => Authentication = value;
        }

        public async Task WriteAsync()
        {
            if (!await SessionIsAliveAsync())
            {
                return;
            }

            Started = true;
            Writes++;
        }
    }

    /// <summary>
    /// `NavigationManager` é abstrato e exige `Initialize` antes de navegar; esta implementação só
    /// registra o destino, porque o que se afirma é para onde a guarda manda e se força recarga.
    /// </summary>
    private sealed class RecordingNavigationManager : NavigationManager
    {
        public RecordingNavigationManager() =>
            Initialize("https://catalogo.local/", "https://catalogo.local/painel/configuracoes");

        public string? LastDestination { get; private set; }

        public bool LastForceLoad { get; private set; }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            LastDestination = uri;
            LastForceLoad = forceLoad;
        }
    }
}
