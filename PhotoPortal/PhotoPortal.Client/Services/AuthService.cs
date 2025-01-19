using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace PhotoPortal.Client.Services;

public class AuthService(
    IEnumerable<HttpClient> _http
    ) : AuthenticationStateProvider
{
    private HttpClient http => _http.FirstOrDefault();

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (http is null)
        {
            return new(new());
        }

        var response = await http.GetAsync("./Login/CheckAuthState");
        try
        {
            response.EnsureSuccessStatusCode();

            return new(new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new(ClaimTypes.NameIdentifier, "User"),
                    new(ClaimTypes.Name, "User")
                ],
                "Cookies")));
        } catch (Exception)
        {
            return new(new());
        }
    }

    public void Reload()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}