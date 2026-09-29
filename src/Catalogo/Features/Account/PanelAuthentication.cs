using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Catalogo.Data;

namespace Catalogo.Features.Account;

public static class PanelAuthentication
{
    /// <summary>Prefixo protegido. Tudo abaixo dele exige autenticação (RN-58).</summary>
    public const string PanelPathPrefix = "/painel";

    public const string LoginPath = "/painel/entrar";

    public const string LogoutPath = "/painel/sair";

    /// <summary>Tentativas antes do bloqueio temporário (RN-60).</summary>
    public const int MaxFailedAccessAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddPanelAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OwnerAccountOptions>(
            configuration.GetSection(OwnerAccountOptions.SectionName));

        services.AddIdentityCore<OwnerAccount>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedAccount = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;
            })
            .AddEntityFrameworkStores<CatalogDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Validação do selo de segurança do cookie. `AddIdentityCore` não a liga — só
        // `AddIdentity`/`AddIdentityCookies` fazem —, e sem ela a troca de senha renova o
        // selo e **nada o confere**: o cookie emitido antes continua abrindo o painel, que
        // é justamente o acesso que o dono quer cortar quando troca a senha por suspeita
        // (R-03 de `REVIEW-T-31-2026-09-29`). Intervalo zero confere a cada requisição; o
        // custo é uma consulta por requisição **autenticada**, e a vitrine é anônima.
        services.Configure<SecurityStampValidatorOptions>(
            options => options.ValidationInterval = TimeSpan.Zero);

        services.AddScoped<ISecurityStampValidator, SecurityStampValidator<OwnerAccount>>();

        // `AddIdentityCookies()` em vez de um `AddCookie` só para o esquema da aplicação:
        // quando o selo não confere, o validador chama `SignInManager.SignOutAsync()`, que
        // desloga **três** esquemas do Identity. Com apenas um registrado, o deslogamento
        // lança e a requisição termina em 500 em vez de cair na tela de acesso — verificado
        // na correção de R-03. Este projeto não tem login externo nem segundo fator, e os
        // esquemas extras ficam sem uso; é o preço de usar o validador do framework em vez
        // de reescrevê-lo.
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.LoginPath = LoginPath;
            options.AccessDeniedPath = LoginPath;
            options.ReturnUrlParameter = ReturnUrlParameter;

            // `Events` não é tocado de propósito: `AddIdentityCookies` já aponta
            // `OnValidatePrincipal` para o validador de selo, e substituir o delegate aqui
            // desligaria justamente o que R-03 pede.
        });

        services.AddAuthorization();

        return services;
    }

    public const string ReturnUrlParameter = "retorno";

    /// <summary>
    /// Fecha o prefixo do painel para quem não está autenticado (RN-58, CA-26). É um
    /// gate por caminho, e não um atributo por componente, porque a regra é sobre a área
    /// inteira — uma tela nova não pode nascer desprotegida por esquecimento.
    /// </summary>
    public static IApplicationBuilder UsePanelAuthorization(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var isPanel = path.StartsWithSegments(PanelPathPrefix, StringComparison.OrdinalIgnoreCase);
            var isLogin = path.StartsWithSegments(LoginPath, StringComparison.OrdinalIgnoreCase);

            if (isPanel && !isLogin && context.User.Identity?.IsAuthenticated != true)
            {
                var returnUrl = Uri.EscapeDataString(path + context.Request.QueryString);
                context.Response.Redirect($"{LoginPath}?{ReturnUrlParameter}={returnUrl}");
                return;
            }

            await next();
        });
}
