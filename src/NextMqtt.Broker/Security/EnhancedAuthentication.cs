using NextMqtt.Broker.Options;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Security;

/// <summary>Enhanced authentication provider selected by the CONNECT authentication method.</summary>
/// <remarks>实现可以是 JWT 校验、外部身份服务或挑战应答；方法名必须与客户端声明一致。</remarks>
public interface IEnhancedAuthenticator
{
    /// <summary>Method name carried in the Authentication Method property.</summary>
    string Method { get; }

    /// <summary>Validates one authentication step.</summary>
    /// <param name="context">Client identity and the data supplied so far.</param>
    /// <param name="cancellationToken">Cancels the provider call.</param>
    /// <returns>Success, a challenge, or a failure reason code.</returns>
    ValueTask<EnhancedAuthResult> AuthenticateAsync(
        EnhancedAuthContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Input for one enhanced authentication step.</summary>
/// <param name="ClientId">Client identifier from CONNECT.</param>
/// <param name="Username">Username from CONNECT, when present.</param>
/// <param name="RemoteAddress">Peer address.</param>
/// <param name="Data">Authentication Data supplied by the client.</param>
public sealed record EnhancedAuthContext(
    string ClientId,
    string? Username,
    string RemoteAddress,
    ReadOnlyMemory<byte> Data);

/// <summary>Outcome of one enhanced authentication step.</summary>
/// <param name="Principal">Identity when authentication finished.</param>
/// <param name="Challenge">Data to send back in an AUTH 0x18 packet, or null.</param>
/// <param name="ReasonCode">0x00 on success, 0x18 for a challenge, otherwise a failure code.</param>
public readonly record struct EnhancedAuthResult(ClientPrincipal? Principal, byte[]? Challenge, byte ReasonCode)
{
    /// <summary>True when a principal was produced.</summary>
    public bool Succeeded => Principal is not null;

    /// <summary>Builds a success result.</summary>
    /// <param name="principal">Authenticated identity.</param>
    public static EnhancedAuthResult Success(ClientPrincipal principal)
        => new(principal, null, MqttReasonCodes.Success);

    /// <summary>Builds a challenge that must be answered with another AUTH packet.</summary>
    /// <param name="challenge">Data sent to the client.</param>
    public static EnhancedAuthResult Continue(byte[] challenge)
        => new(null, challenge, MqttReasonCodes.ContinueAuthentication);

    /// <summary>Builds a failure result.</summary>
    /// <param name="reasonCode">CONNACK or AUTH reason code.</param>
    public static EnhancedAuthResult Failure(byte reasonCode)
        => new(null, null, reasonCode);
}

/// <summary>
/// Built-in provider that treats Authentication Data as an HS256 JWT signed with
/// the configured JWT secret.
/// </summary>
public sealed class JwtEnhancedAuthenticator : IEnhancedAuthenticator
{
    /// <summary>Method name clients must send in CONNECT.</summary>
    public const string MethodName = "nextmqtt-jwt";

    private readonly JwtValidator _validator;

    /// <summary>Creates the provider from the JWT configuration.</summary>
    /// <param name="options">Secret, issuer, audience and claim names.</param>
    public JwtEnhancedAuthenticator(JwtOptions options)
    {
        _validator = new JwtValidator(options);
    }

    /// <summary>Always <see cref="MethodName"/>.</summary>
    public string Method => MethodName;

    /// <summary>Validates the token carried in Authentication Data.</summary>
    /// <param name="context">Client identity and token bytes.</param>
    /// <param name="cancellationToken">Ignored; validation is synchronous.</param>
    public ValueTask<EnhancedAuthResult> AuthenticateAsync(
        EnhancedAuthContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Data.IsEmpty)
        {
            return ValueTask.FromResult(EnhancedAuthResult.Failure(MqttReasonCodes.NotAuthorized));
        }

        var token = System.Text.Encoding.UTF8.GetString(context.Data.Span);

        if (_validator.TryValidate(token, out var principal, out _))
        {
            return ValueTask.FromResult(EnhancedAuthResult.Success(
                principal with { ClientId = context.ClientId }));
        }

        return ValueTask.FromResult(EnhancedAuthResult.Failure(MqttReasonCodes.NotAuthorized));
    }
}
