using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OperatorConsole.Models;
using OperatorConsole.Services;

namespace OperatorConsole.Controllers;

[AllowAnonymous]
public class AccountController(IConfiguration config, ILogger<AccountController> logger) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Links");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var expectedUser = config["Operator:Username"] ?? "";
        var userMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(model.Username), Encoding.UTF8.GetBytes(expectedUser));
        var passwordMatches = PasswordHasher.Verify(model.Password, config["Operator:PasswordHash"]);

        if (!userMatches || !passwordMatches || expectedUser.Length == 0)
        {
            logger.LogWarning("Failed operator login for {Username}", model.Username);
            ModelState.AddModelError("", "Invalid username or password.");
            model.Password = "";
            return View(model);
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, expectedUser)], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        logger.LogInformation("Operator {Username} logged in", expectedUser);

        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToAction("Index", "Links");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }
}
