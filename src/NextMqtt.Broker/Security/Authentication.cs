using System.Security.Cryptography;
using System.Text;
using NextMqtt.Core.Protocol;
using NextMqtt.Broker.Options;

namespace NextMqtt.Broker.Security;

/// <summary>Authenticated identity attached to a session.</summary>
public sealed record ClientPrincipal(
    string Name,
    string? Username,
    string? ClientId,
    bool IsSuperuser,
    IReadOnlyDictionary<string, string> Claims)
{
    public static ClientPrincipal Anonymous(string clientId) =>
        new(clientId, null, clientId, IsSuperuser: false, new Dictionary<string, string>());

    public static ClientPrincipal Create(string username, string clientId, bool isSuperuser, IReadOnlyDictionary<string, string>? claims = null) =>
        new(username, username, clientId, isSuperuser, claims ?? new Dictionary<string, string>());
}

/// <summary>Input supplied by the client during CONNECT.</summary>
public sealed record ClientAuthenticationContext(
    string ClientId,
    string? Username,
    ReadOnlyMemory<byte> Password,
    string RemoteAddress,
    MqttProtocolVersion ProtocolVersion);

/// <summary>Authentication outcome. A non-null principal indicates success.</summary>
public readonly record struct AuthenticationResult(ClientPrincipal? Principal, byte ReasonCode)
{
    public bool Succeeded => Principal is not null;

    public static AuthenticationResult Success(ClientPrincipal principal) =>
        new(principal, MqttReasonCodes.Success);

    public static AuthenticationResult Failure(byte reasonCode) =>
        new(null, reasonCode);
}

public interface IClientAuthenticator
{
    ValueTask<AuthenticationResult> AuthenticateAsync(
        ClientAuthenticationContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>PBKDF2-SHA256 password hashing with a versioned, self-describing format.</summary>
public static class PasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public const int DefaultIterations = 210_000;

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 10_000);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(encoded))
        {
            return false;
        }

        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations < 10_000)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>
/// Password, JWT and anonymous authentication in that order. JWT tokens are
/// sent in the MQTT password field.
/// </summary>
public sealed class ClientAuthenticator : IClientAuthenticator
{
    private readonly Dictionary<string, LocalUserOptions> _users;
    private readonly HashSet<string> _superusers;
    private readonly JwtValidator? _jwt;
    private readonly bool _allowAnonymous;

    public ClientAuthenticator(AuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _users = new Dictionary<string, LocalUserOptions>(StringComparer.Ordinal);

        foreach (var user in options.Users)
        {
            if (!string.IsNullOrWhiteSpace(user.Username))
            {
                _users[user.Username] = user;
            }
        }

        _superusers = new HashSet<string>(options.Superusers, StringComparer.Ordinal);
        _allowAnonymous = options.AllowAnonymous || (_users.Count == 0 && !options.Jwt.Enabled);
        _jwt = options.Jwt.Enabled ? new JwtValidator(options.Jwt) : null;
    }

    public ValueTask<AuthenticationResult> AuthenticateAsync(
        ClientAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        var password = DecodePassword(context.Password);

        if (!string.IsNullOrEmpty(context.Username)
            && _users.TryGetValue(context.Username, out var user)
            && PasswordHasher.Verify(password, user.PasswordHash))
        {
            var isSuperuser = _superusers.Contains(user.Username)
                || user.Roles.Contains("admin", StringComparer.OrdinalIgnoreCase);

            return ValueTask.FromResult(AuthenticationResult.Success(
                ClientPrincipal.Create(user.Username, context.ClientId, isSuperuser)));
        }

        if (_jwt is not null
            && !string.IsNullOrEmpty(password)
            && _jwt.TryValidate(password, out var jwtPrincipal, out _))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isSuperuser = _superusers.Contains(jwtPrincipal.Username ?? string.Empty)
                || jwtPrincipal.IsSuperuser;

            return ValueTask.FromResult(AuthenticationResult.Success(
                jwtPrincipal with { ClientId = context.ClientId, IsSuperuser = isSuperuser }));
        }

        if (_allowAnonymous && string.IsNullOrEmpty(context.Username) && string.IsNullOrEmpty(password))
        {
            return ValueTask.FromResult(AuthenticationResult.Success(
                ClientPrincipal.Anonymous(context.ClientId)));
        }

        return ValueTask.FromResult(AuthenticationResult.Failure(MqttReasonCodes.BadUserNameOrPassword));
    }

    private static string DecodePassword(ReadOnlyMemory<byte> password)
    {
        if (password.IsEmpty)
        {
            return string.Empty;
        }

        try
        {
            return new UTF8Encoding(false, false).GetString(password.Span);
        }
        catch (DecoderFallbackException)
        {
            return string.Empty;
        }
    }
}
