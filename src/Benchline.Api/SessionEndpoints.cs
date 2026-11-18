using System.Security.Claims;
using Benchline.Contracts;
using Benchline.Domain;
using Benchline.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Benchline.Api;

internal static class SessionEndpoints
{
    public static void MapSession(this RouteGroupBuilder api)
    {
        api.MapGet("/token", (HttpContext context, IAntiforgery antiforgery) =>
            new TokenDto(antiforgery.GetAndStoreTokens(context).RequestToken!))
            .WithSummary("Acquire a write token for the current browser session.");
        api.MapPost("/session", SignIn).RequireRateLimiting("sign-in").AddEndpointFilter<RequestTokenFilter>()
            .WithSummary("Open a cookie session with a workshop account.");
        api.MapGet("/session", (ClaimsPrincipal principal, IWebHostEnvironment environment) =>
        {
            var name = principal.Identity!.Name!;
            if (environment.IsEnvironment("Demo") && principal.FindFirstValue(ClaimTypes.NameIdentifier) is { } id)
                name = DemoCatalog.DisplayName(id) ?? name;
            return new SessionDto(name, principal.FindFirstValue(ClaimTypes.Role)!);
        }).RequireAuthorization();
        api.MapPost("/sign-out", async (HttpContext context) =>
        {
            await context.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization().AddEndpointFilter<RequestTokenFilter>();
    }

    public static Actor Actor(ClaimsPrincipal principal) => new(
        principal.FindFirstValue(ClaimTypes.NameIdentifier)!,
        Enum.Parse<Role>(principal.FindFirstValue(ClaimTypes.Role)!));

    private static async Task<IResult> SignIn(SignInRequest input, HttpContext context,
        WorkshopDb db, IPasswordHasher<Account> hasher, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.UserName) || input.UserName.Length > 80
            || string.IsNullOrEmpty(input.Password) || input.Password.Length > 200)
            return Results.Problem("Enter a valid workshop username and password.", statusCode: 400);

        var username = input.UserName.Trim().ToLowerInvariant();
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.UserName == username, cancellationToken);
        if (account is null || hasher.VerifyHashedPassword(account, account.PasswordHash, input.Password) == PasswordVerificationResult.Failed)
            return Results.Problem("The username or password was incorrect.", statusCode: 401);

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id),
            new Claim(ClaimTypes.Name, account.Name),
            new Claim(ClaimTypes.Role, account.Role.ToString())
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(new ClaimsPrincipal(identity));
        return Results.Ok(new SessionDto(account.Name, account.Role.ToString()));
    }
}

public sealed class RequestTokenFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
            return Results.Problem("The session token expired. Refresh and try again.", statusCode: 400);
        return await next(context);
    }
}
