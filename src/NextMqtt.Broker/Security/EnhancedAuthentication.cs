using NextMqtt.Broker.Options;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Security;

/// <summary>Enhanced authentication provider selected by the CONNECT authentication method.</summary>
/// <remarks>实现可以是 JWT 校验、外部身份服务或挑战应答；方法名必须与客户端声明一致。</remarks>
public interface IEnhancedAuthenticator
{
    /// <summary>Method name carried in the Authentication Method property.</summary>
    /// <remarks>客户端在 CONNECT 里声明，大小写敏感。</remarks>
    string Method { get; }

    /// <summary>Validates one authentication step.</summary>
    /// <remarks>可以返回挑战继续下一轮，取消令牌会中断外部查询。</remarks>
    /// <param name="context">Client identity and the data supplied so far.</param>
    /// <param name="cancellationToken">Cancels the provider call.</param>
    /// <returns>Success, a challenge, or a failure reason code.</returns>
    ValueTask<EnhancedAuthResult> AuthenticateAsync(
        EnhancedAuthContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Input for one enhanced authentication step.</summary>
/// <remarks>Data 是客户端原样给出的字节，不假设编码。</remarks>
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
/// <remarks>三种形态：完成、挑战、失败。</remarks>
/// <param name="Principal">Identity when authentication finished.</param>
/// <param name="Challenge">Data to send back in an AUTH 0x18 packet, or null.</param>
/// <param name="ReasonCode">0x00 on success, 0x18 for a challenge, otherwise a failure code.</param>
public readonly record struct EnhancedAuthResult(ClientPrincipal? Principal, byte[]? Challenge, byte ReasonCode)
{
    /// <summary>True when a principal was produced.</summary>
    /// <remarks>挑战阶段为 false，但不算失败。</remarks>
    public bool Succeeded => Principal is not null;

    /// <summary>Builds a success result.</summary>
    /// <remarks>原因码 0x00，Challenge 为空。</remarks>
    /// <param name="principal">Authenticated identity.</param>
    public static EnhancedAuthResult Success(ClientPrincipal principal)
        => new(principal, null, MqttReasonCodes.Success);

    /// <summary>Builds a challenge that must be answered with another AUTH packet.</summary>
    /// <remarks>原因码 0x18，客户端必须以 AUTH 继续，否则按协议错误断开。</remarks>
    /// <param name="challenge">Data sent to the client.</param>
    public static EnhancedAuthResult Continue(byte[] challenge)
        => new(null, challenge, MqttReasonCodes.ContinueAuthentication);

    /// <summary>Builds a failure result.</summary>
    /// <remarks>原因码会写进 CONNACK，或写进 AUTH 的错误响应。</remarks>
    /// <param name="reasonCode">CONNACK or AUTH reason code.</param>
    public static EnhancedAuthResult Failure(byte reasonCode)
        => new(null, null, reasonCode);
}

/// <summary>
/// Built-in provider that treats Authentication Data as an HS256 JWT signed with
/// the configured JWT secret.
/// </summary>
/// <remarks>认证数据为空直接拒绝；校验失败统一回 0x87，不透露是签名问题还是声明问题。</remarks>
public sealed class JwtEnhancedAuthenticator : IEnhancedAuthenticator
{
    /// <summary>Method name clients must send in CONNECT.</summary>
    /// <remarks>固定为 nextmqtt-jwt，改名会让现有客户端全部失败。</remarks>
    public const string MethodName = "nextmqtt-jwt";

    /// <summary>Validator built once from the JWT options.</summary>
    private readonly JwtValidator _validator;

    /// <summary>Creates the provider from the JWT configuration.</summary>
    /// <remarks>没有密钥时构造会抛异常，调用方应先判断是否启用。</remarks>
    /// <param name="options">Secret, issuer, audience and claim names.</param>
    public JwtEnhancedAuthenticator(JwtOptions options)
    {
        _validator = new JwtValidator(options);
    }

    /// <summary>Always <see cref="MethodName"/>.</summary>
    /// <remarks>返回常量，不读取配置。</remarks>
    public string Method => MethodName;

    /// <summary>Validates the token carried in Authentication Data.</summary>
    /// <remarks>只做一次校验，不返回挑战；空数据直接按未授权拒绝。</remarks>
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
