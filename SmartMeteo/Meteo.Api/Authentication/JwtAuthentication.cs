using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Meteo.Api.Authentication;

public sealed class AuthSettings
{
    public JwtSettings Jwt { get; init; } = new();
    public List<AuthUserSettings> Users { get; init; } = [];
}

public sealed class JwtSettings
{
    public string Issuer { get; init; } = "SmartMeteo.Api";
    public string Audience { get; init; } = "SmartMeteo.Client";
    public string SigningKey { get; init; } = string.Empty;
    public int ExpirationMinutes { get; init; } = 60;
}

public sealed class AuthUserSettings
{
    public string Username { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string PasswordSalt { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}

public sealed record LoginRequest
{
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string Username { get; init; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;
}

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration.GetSection("Authentication").Get<AuthSettings>()
            ?? throw new InvalidOperationException("Authentication configuration is missing.");

        if (string.IsNullOrWhiteSpace(settings.Jwt.SigningKey) ||
            Encoding.UTF8.GetByteCount(settings.Jwt.SigningKey) < 32 ||
            settings.Jwt.SigningKey.Contains("change-in-production", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Authentication:Jwt:SigningKey must be a unique secret of at least 32 bytes.");

        if (settings.Jwt.ExpirationMinutes is < 5 or > 1440)
            throw new InvalidOperationException("Authentication:Jwt:ExpirationMinutes must be between 5 and 1440.");

        services.Configure<AuthSettings>(configuration.GetSection("Authentication"));
        services.AddSingleton<AuthSettingsProvider>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("login", limiter =>
            {
                limiter.PermitLimit = 5;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = settings.Jwt.Issuer,
                    ValidAudience = settings.Jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Jwt.SigningKey)),
                    ClockSkew = TimeSpan.Zero,
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.Name
                };

                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new { error = "Forbidden" });
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("CalibrationAdmin", policy =>
                policy.RequireAuthenticatedUser().RequireRole("admin"));
        });

        return services;
    }

    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", (LoginRequest request, AuthSettingsProvider auth) =>
        {
            var user = auth.Settings.Users.FirstOrDefault(existingUser =>
                string.Equals(existingUser.Username, request.Username, StringComparison.Ordinal) &&
                VerifyPassword(request.Password, existingUser.PasswordHash, existingUser.PasswordSalt));

            if (user is null)
                return Results.Unauthorized();

            var now = DateTime.UtcNow;
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Username),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var signingKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(auth.Settings.Jwt.SigningKey));
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: auth.Settings.Jwt.Issuer,
                audience: auth.Settings.Jwt.Audience,
                claims: claims,
                notBefore: now,
                expires: now.AddMinutes(auth.Settings.Jwt.ExpirationMinutes),
                signingCredentials: credentials);

            var serializedToken = new JwtSecurityTokenHandler().WriteToken(token);

            return Results.Ok(new
            {
                accessToken = serializedToken,
                tokenType = "Bearer",
                expiresIn = auth.Settings.Jwt.ExpirationMinutes * 60,
                role = user.Role
            });
        })
        .RequireRateLimiting("login");

        return endpoints;
    }

    private static bool VerifyPassword(string password, string passwordHash, string passwordSalt)
    {
        try
        {
            var salt = Convert.FromBase64String(passwordSalt);
            var expectedHash = Convert.FromBase64String(passwordHash);

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                100_000,
                HashAlgorithmName.SHA256,
                32);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class AuthSettingsProvider
{
    public AuthSettings Settings { get; }

    public AuthSettingsProvider(Microsoft.Extensions.Options.IOptions<AuthSettings> options)
    {
        Settings = options.Value;
    }
}
