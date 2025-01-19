using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace PhotoPortal.Controllers;

public class AuthController(
    IConfiguration config
    ) : Controller
{
    [AllowAnonymous]
    [HttpGet("/Login/CheckAuthState")]
    public IActionResult CheckAuthState()
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            return Ok();
        }

        return StatusCode(StatusCodes.Status500InternalServerError);
    }

    [AllowAnonymous]
    [HttpPost("/Login/Submit")]
    public async Task<IActionResult> LogIn(string password)
    {
        if (password != config["Passphrase"])
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        await HttpContext.SignInAsync(
            new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new(ClaimTypes.NameIdentifier, "User"),
                    new(ClaimTypes.Name, "User")
                ],
                CookieAuthenticationDefaults.AuthenticationScheme)));

        return Ok();
    }
}
