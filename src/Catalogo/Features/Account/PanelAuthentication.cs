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

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.LoginPath = LoginPath;
                options.AccessDeniedPath = LoginPath;
                options.ReturnUrlParameter = ReturnUrlParameter;
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
