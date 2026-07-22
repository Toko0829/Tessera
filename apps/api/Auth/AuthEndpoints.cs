using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Tessera.Persistence;

namespace Tessera.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/register", RegisterAsync).RequireRateLimiting(RateLimitPolicies.AuthRegister);
        group.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimitPolicies.AuthLogin);
        group.MapGet("/me", GetMe).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<TesseraUser> userManager,
        TimeProvider clock)
    {
        var errors = Validate(request.Email, request.Password);
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        var user = new TesseraUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            CreatedAt = clock.GetUtcNow(),
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // Do not reveal whether the email is already taken; that would let an
            // attacker enumerate accounts. Same generic message for every failure.
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["registration"] = ["Could not create an account with the details provided."],
            });
        }

        return Results.Created($"/auth/users/{user.Id}", new { user.Id });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<TesseraUser> userManager,
        SignInManager<TesseraUser> signInManager,
        TokenService tokens)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                var token = tokens.CreateAccessToken(user);
                return Results.Ok(new LoginResponse(token.Value, token.ExpiresAt));
            }
        }

        // One generic response whether the account is missing, the password is
        // wrong, or the account is locked. Never leak which.
        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials.");
    }

    private static IResult GetMe(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);

        return Guid.TryParse(sub, out var id)
            ? Results.Ok(new MeResponse(id, email ?? string.Empty))
            : Results.Unauthorized();
    }

    private static Dictionary<string, string[]>? Validate(string email, string password)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (string.IsNullOrEmpty(password) || password.Length < 12)
        {
            errors["password"] = ["Password must be at least 12 characters."];
        }

        return errors.Count == 0 ? null : errors;
    }
}
