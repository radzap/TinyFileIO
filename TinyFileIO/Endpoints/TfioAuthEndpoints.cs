using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using TinyFileIO.Data;
using TinyFileIO.Models.Entities;
using TinyFileIO.Services;

namespace TinyFileIO.Endpoints;

public static class TfioAuthEndpoints
{
    public static void MapTfioAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/_tfio/auth");

        // POST /_tfio/auth/login
        group.MapPost("/login", async (HttpContext ctx, IAuthorizationProvider auth) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var username = form["Username"].ToString().Trim();
            var password = form["Password"].ToString();
            var returnUrl = form["ReturnUrl"].ToString();

            var identity = await auth.AuthenticateAsync(username, password);
            if (identity is null)
                return Results.Redirect("/_tfio/login?error=1");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, identity.UserId),
                new(ClaimTypes.Name, identity.Username),
                new("is_super_admin", identity.IsSuperAdmin.ToString().ToLowerInvariant())
            };
            var principal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties { IsPersistent = false });

            var redirect = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/')
                ? returnUrl
                : "/_tfio";

            return Results.Redirect(redirect);
        }).DisableAntiforgery();

        // POST /_tfio/auth/setup  — only allowed when no DB users exist
        group.MapPost("/setup", async (HttpContext ctx, IDbContextFactory<AppDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            if (await db.Users.AnyAsync())
                return Results.Redirect("/_tfio/login");

            var form = await ctx.Request.ReadFormAsync();
            var username = form["Username"].ToString().Trim();
            var password = form["Password"].ToString();
            var confirm  = form["Confirm"].ToString();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return Results.Redirect("/_tfio/setup?error=empty");

            if (password != confirm)
                return Results.Redirect("/_tfio/setup?error=mismatch");

            db.Users.Add(new User
            {
                Username     = username,
                Password     = password,
                IsSuperAdmin = true
            });
            await db.SaveChangesAsync();

            return Results.Redirect("/_tfio/login?created=1");
        }).DisableAntiforgery();

        // GET /_tfio/auth/logout
        group.MapGet("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/_tfio/login");
        });
    }
}
