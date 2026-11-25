using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Security;
using NextMqtt.Server.Options;

namespace NextMqtt.Server.Security;

/// <summary>
/// Dashboard authentication: PBKDF2 password login issues a short-lived
/// HS256 token; API keys are accepted through the X-API-Key header.
/// </summary>
/// <summary>Dashboard authentication: PBKDF2 login, HS256 tokens and API keys.</summary>
public sealed class AdminAuthService
{
    private readonly string _username;
    private readonly string _passwordHash;
    private readonly JwtValidator _validator;
    private readonly string _tokenSecret;
    private readonly int _tokenLifetimeMinutes;
    private readonly HashSet<string> _apiKeyHashes;
    private readonly ILogger _logger;

    private AdminAuthService(
        string username,
        string passwordHash,
        string tokenSecret,
        int tokenLifetimeMinutes,
        IEnumerable<string> apiKeyHashes,
        ILogger logger)
    {
        _username = username;
        _passwordHash = passwordHash;
        _tokenSecret = tokenSecret;
        _tokenLifetimeMinutes = tokenLifetimeMinutes;
        _apiKeyHashes = new HashSet<string>(apiKeyHashes, StringComparer.OrdinalIgnoreCase);
        _logger = logger;

        _validator = new JwtValidator(new JwtOptions
        {
            Secret = tokenSecret,
            RequireExpiration = true,
        });
    }

    /// <summary>Builds the service from configuration and environment overrides.</summary>
    /// <param name="options">Admin username, hash, token secret and API key digests.</param>
    /// <param name="logger">Logger used for bootstrap warnings.</param>
    public static AdminAuthService Create(AdminOptions options, ILogger logger)
    {
        var username = Environment.GetEnvironmentVariable("NEXTMQTT_ADMIN_USERNAME") ?? options.Username;

        var passwordHash = options.PasswordHash;
        var environmentPassword = Environment.GetEnvironmentVariable("NEXTMQTT_ADMIN_PASSWORD");

        if (!string.IsNullOrEmpty(environmentPassword))
        {
            passwordHash = PasswordHasher.Hash(environmentPassword);
        }
        else if (string.IsNullOrEmpty(passwordHash))
        {
            var generated = GenerateSecret(18);
            passwordHash = PasswordHasher.Hash(generated);
            logger.LogWarning(
                "No admin password configured. Generated bootstrap password for user '{Username}': {Password}",
                username,
                generated);
            logger.LogWarning("Set NEXTMQTT_ADMIN_PASSWORD before exposing the dashboard.");
        }

        var tokenSecret = Environment.GetEnvironmentVariable("NEXTMQTT_ADMIN_TOKEN_SECRET") ?? options.TokenSecret;

        if (string.IsNullOrEmpty(tokenSecret))
        {
            tokenSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            logger.LogWarning("No admin token secret configured. Generated an ephemeral signing key; tokens will not survive a restart.");
        }

        return new AdminAuthService(
            username,
            passwordHash!,
            tokenSecret,
            Math.Clamp(options.TokenLifetimeMinutes, 5, 10_080),
            options.ApiKeySha256,
            logger);
    }

    /// <summary>Verifies a username/password pair in constant time.</summary>
    /// <param name="username">Submitted username.</param>
    /// <param name="password">Submitted plaintext password.</param>
    public bool ValidateCredentials(string username, string password)
        => string.Equals(username, _username, StringComparison.Ordinal)
           && PasswordHasher.Verify(password, _passwordHash);

    /// <summary>Issues a bearer token for the configured admin user.</summary>
    /// <param name="expiresAt">Expiry written into the token.</param>
    /// <returns>The encoded JWT.</returns>
    public string IssueToken(out DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        expiresAt = now.AddMinutes(_tokenLifetimeMinutes);

        var payload = new Dictionary<string, object>
        {
            ["sub"] = _username,
            ["roles"] = new[] { "admin" },
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["iss"] = "nextmqtt",
        };

        var header = Base64Url(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = Base64Url(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_tokenSecret),
            Encoding.ASCII.GetBytes($"{header}.{body}")));

        return $"{header}.{body}.{signature}";
    }

    /// <summary>Authenticates a request by bearer token or API key.</summary>
    /// <param name="request">Incoming HTTP request.</param>
    /// <returns>Principal on success, null otherwise.</returns>
    public ClientPrincipal? Authenticate(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-API-Key", out var apiKey))
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey.ToString())));

            if (_apiKeyHashes.Contains(hash))
            {
                return ClientPrincipal.Create("api-key", string.Empty, isSuperuser: true);
            }
        }

        var authorization = request.Headers.Authorization.ToString();

        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization["Bearer ".Length..].Trim();

            if (_validator.TryValidate(token, out var principal, out _))
            {
                return principal with { ClientId = string.Empty, IsSuperuser = true };
            }
        }

        return null;
    }

    /// <summary>Validates an HS256 token and rebuilds the principal.</summary>
    /// <param name="token">JWT without the <c>Bearer</c> prefix.</param>
    public ClientPrincipal? ValidateToken(string token)
        => _validator.TryValidate(token, out var principal, out _)
            ? principal with { ClientId = string.Empty, IsSuperuser = true }
            : null;

    /// <summary>Hashes an API key for comparison with configured digests.</summary>
    /// <param name="apiKey">Plaintext key.</param>
    public static string HashApiKey(string apiKey)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    private static string GenerateSecret(int bytes)
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
        return value.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
