using System.Net;

namespace Catalogo.Tests;

/// <summary>
/// Mantém os cookies entre requisições da mesma sessão de teste e segue redirecionamentos.
/// É o que permite exercer o fluxo real de autenticação por cookie, incluindo o
/// antiforgery do formulário e o gate do painel.
/// </summary>
public sealed class CookieHandler : DelegatingHandler
{
    private const int MaxRedirects = 5;

    private readonly CookieContainer cookies = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await SendWithCookiesAsync(request, cancellationToken);

        for (var redirect = 0; redirect < MaxRedirects && IsRedirect(response); redirect++)
        {
            var location = new Uri(request.RequestUri!, response.Headers.Location!);
            var follow = new HttpRequestMessage(HttpMethod.Get, location);

            response.Dispose();
            response = await SendWithCookiesAsync(follow, cancellationToken);
            request = follow;
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendWithCookiesAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;

        var header = cookies.GetCookieHeader(uri);
        if (!string.IsNullOrEmpty(header))
        {
            request.Headers.Add("Cookie", header);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                cookies.SetCookies(uri, value);
            }
        }

        return response;
    }

    private static bool IsRedirect(HttpResponseMessage response) =>
        response.Headers.Location is not null
        && response.StatusCode is HttpStatusCode.Found
            or HttpStatusCode.MovedPermanently
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect;
}
