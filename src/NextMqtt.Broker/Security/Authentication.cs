using System.Security.Cryptography;
using System.Text;
using NextMqtt.Core.Protocol;
using NextMqtt.Broker.Options;

namespace NextMqtt.Broker.Security;

/// <summary>Authenticated identity attached to a session.</summary>
/// <param name="Name">Display name; the username or client id.</param>
/// <param name="Username">Authenticated username, null for anonymous.</param>
/// <param name="ClientId">Client identifier the principal is bound to.</param>
/// <param name="IsSuperuser">True when ACL checks are bypassed.</param>
/// <param name="Claims">Extra claims for ACL placeholders.</param>
public sealed record ClientPrincipal(
    string Name,
    string? Username,
    string? ClientId,
    bool IsSuperuser,
    IReadOnlyDictionary<string, string> Claims)
{
    /// <summary>Creates the anonymous principal used when no credentials are sent.</summary>
    /// <param name="clientId">Client identifier.</param>
    public static ClientPrincipal Anonymous(string clientId) =>
        new(clientId, null, clientId, IsSuperuser: false, new Dictionary<string, string>());

    /// <summary>Creates a principal from a validated username.</summary>
    /// <param name="username">Authenticated username.</param>
    /// <param name="clientId">Client identifier.</param>
    /// <param name="isSuperuser">True when ACL checks are bypassed.</param>
    /// <param name="claims">Optional claims.</param>
    public static ClientPrincipal Create(string username, string clientId, bool isSuperuser, IReadOnlyDictionary<string, string>? claims = null) =>
        new(username, username, clientId, isSuperuser, claims ?? new Dictionary<string, string>());
}

/// <summary>Input supplied by the client during CONNECT.</summary>
/// <param name="ClientId">Client identifier from CONNECT.</param>
/// <param name="Username">Username from CONNECT, when present.</param>
/// <param name="Password">Raw password bytes from CONNECT.</param>
/// <param name="RemoteAddress">Peer address, used by address-based policies.</param>
/// <param name="ProtocolVersion">Protocol level of the connection.</param>
public sealed record ClientAuthenticationContext(
    string ClientId,
    string? Username,
    ReadOnlyMemory<byte> Password,
    string RemoteAddress,
    MqttProtocolVersion ProtocolVersion);

/// <summary>Authentication outcome. A non-null principal indicates success.</summary>
/// <param name="Principal">Identity on success, null on failure.</param>
/// <param name="ReasonCode">0x00 or the CONNACK reason code to return.</param>
public readonly record struct AuthenticationResult(ClientPrincipal? Principal, byte ReasonCode)
{
    /// <summary>True when a principal was produced.</summary>
    public bool Succeeded => Principal is not null;

    /// <summary>Builds a success result.</summary>
    /// <param name="principal">Authenticated identity.</param>
    public static AuthenticationResult Success(ClientPrincipal principal) =>
        new(principal, MqttReasonCodes.Success);

    /// <summary>Builds a failure result with the CONNACK reason code.</summary>
    /// <param name="reasonCode">Code returned to the client.</param>
    public static AuthenticationResult Failure(byte reasonCode) =>
        new(null, reasonCode);
}

/// <summary>Validates CONNECT credentials.</summary>
public interface IClientAuthenticator
{
    /// <summary>Validates one CONNECT.</summary>
    /// <param name="context">Identity and credentials.</param>
    /// <param name="cancellationToken">Cancels remote lookups.</param>
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

    /// <summary>Default PBKDF2 work factor (210,000 SHA-256 iterations).</summary>
    public const int DefaultIterations = 210_000;

    /// <summary>Hashes a password with a fresh random salt.</summary>
    /// <remarks>输出格式：pbkdf2-sha256（Base64）。</remarks>
    /// <param name="password">Plaintext password.</param>
    /// <param name="iterations">Work factor; at least 10,000.</param>
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

    /// <summary>Verifies a password against a stored hash in constant time.</summary>
    /// <param name="password">Plaintext candidate.</param>
    /// <param name="encoded">Stored hash produced by <see cref="Hash"/>.</param>
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

    /// <summary>Builds the authenticator from configuration.</summary>
    /// <param name="options">Users, superusers, JWT settings and anonymous policy.</param>
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
        _allowAnonymous = options.AllowAnonymous;
        _jwt = options.Jwt.Enabled ? new JwtValidator(options.Jwt) : null;
    }

    /// <summary>Tries password, then JWT, then anonymous access, in that order.</summary>
    /// <remarks>JWT 以 MQTT 密码字段传输；全部失败时返回 0x86。</remarks>
    /// <param name="context">Credentials from CONNECT.</param>
    /// <param name="cancellationToken">Cancels the JWT path.</param>
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

    /// <summary>Decodes password bytes as UTF-8, returning empty on invalid input.</summary>
    /// <param name="password">Raw bytes from CONNECT.</param>
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
