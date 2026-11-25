using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NextMqtt.Broker.Options;

namespace NextMqtt.Broker.Security;

/// <summary>Validates compact HS256 JWTs used as MQTT passwords.</summary>
/// <remarks>只支持 HS256 与紧凑格式，不支持 JWKS 与 RS256。</remarks>
public sealed class JwtValidator
{
    /// <summary>Claim names plus the issuer and audience expectations.</summary>
    private readonly JwtOptions _options;
    /// <summary>HMAC key bytes, derived once from the secret.</summary>
    private readonly byte[] _key;

    /// <summary>Prepares the HMAC key from the configured secret.</summary>
    /// <remarks>密钥按 UTF-8 原始字节使用，不做 Base64 解码。</remarks>
    /// <param name="options">Secret, issuer, audience and claim names.</param>
    public JwtValidator(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.Secret))
        {
            throw new ArgumentException("JWT secret must be configured.", nameof(options));
        }

        _options = options;
        _key = Encoding.UTF8.GetBytes(options.Secret);
    }

    /// <summary>Validates signature, time claims and builds the principal.</summary>
    /// <remarks>先验签再读声明，签名错误时不会解析出任何身份。</remarks>
    /// <param name="token">Compact JWS from the password field.</param>
    /// <param name="principal">Principal when validation succeeds.</param>
    /// <param name="error">Reason when it fails.</param>
    public bool TryValidate(string token, out ClientPrincipal principal, out string? error)
    {
        principal = null!;
        error = null;

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            error = "JWT must have three segments.";
            return false;
        }

        if (!TryDecode(parts[0], out var headerBytes)
            || !TryDecode(parts[1], out var payloadBytes)
            || !TryDecode(parts[2], out var signature))
        {
            error = "JWT segments are not valid base64url.";
            return false;
        }

        try
        {
            using var header = JsonDocument.Parse(headerBytes);
            if (!header.RootElement.TryGetProperty("alg", out var alg)
                || !string.Equals(alg.GetString(), "HS256", StringComparison.Ordinal))
            {
                error = "Only HS256 is supported.";
                return false;
            }
        }
        catch (JsonException)
        {
            error = "JWT header is not valid JSON.";
            return false;
        }

        var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var expected = HMACSHA256.HashData(_key, signed);

        if (!CryptographicOperations.FixedTimeEquals(expected, signature))
        {
            error = "JWT signature is invalid.";
            return false;
        }

        JsonDocument payload;
        try
        {
            payload = JsonDocument.Parse(payloadBytes);
        }
        catch (JsonException)
        {
            error = "JWT payload is not valid JSON.";
            return false;
        }

        using (payload)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (_options.RequireExpiration
                && (!payload.RootElement.TryGetProperty("exp", out var expElement)
                    || !expElement.TryGetInt64(out var exp)))
            {
                error = "JWT exp claim is required.";
                return false;
            }

            if (payload.RootElement.TryGetProperty("exp", out var expValue)
                && expValue.TryGetInt64(out var expiresAt)
                && expiresAt <= now)
            {
                error = "JWT is expired.";
                return false;
            }

            if (payload.RootElement.TryGetProperty("nbf", out var nbfValue)
                && nbfValue.TryGetInt64(out var notBefore)
                && notBefore > now + 30)
            {
                error = "JWT is not yet valid.";
                return false;
            }

            if (!string.IsNullOrEmpty(_options.Issuer)
                && (!payload.RootElement.TryGetProperty("iss", out var iss)
                    || !string.Equals(iss.GetString(), _options.Issuer, StringComparison.Ordinal)))
            {
                error = "JWT issuer does not match.";
                return false;
            }

            if (!string.IsNullOrEmpty(_options.Audience) && !HasAudience(payload.RootElement))
            {
                error = "JWT audience does not match.";
                return false;
            }

            if (!payload.RootElement.TryGetProperty(_options.UsernameClaim, out var subject)
                || string.IsNullOrEmpty(subject.GetString()))
            {
                error = "JWT subject claim is missing.";
                return false;
            }

            var username = subject.GetString()!;
            var roles = ReadRoles(payload.RootElement);
            var isSuperuser = roles.Contains("admin", StringComparer.OrdinalIgnoreCase);

            var claims = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sub"] = username,
            };

            foreach (var role in roles)
            {
                claims[$"role:{role}"] = "true";
            }

            principal = ClientPrincipal.Create(username, string.Empty, isSuperuser, claims);
            return true;
        }
    }

    /// <summary>Accepts aud as a string or an array and compares it exactly.</summary>
    private bool HasAudience(JsonElement root)
    {
        if (!root.TryGetProperty("aud", out var audience))
        {
            return false;
        }

        if (audience.ValueKind == JsonValueKind.String)
        {
            return string.Equals(audience.GetString(), _options.Audience, StringComparison.Ordinal);
        }

        if (audience.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in audience.EnumerateArray())
            {
                if (string.Equals(item.GetString(), _options.Audience, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Reads roles from a space-separated string or a string array.</summary>
    private List<string> ReadRoles(JsonElement root)
    {
        var roles = new List<string>();

        if (!root.TryGetProperty(_options.RoleClaim, out var claim))
        {
            return roles;
        }

        if (claim.ValueKind == JsonValueKind.String)
        {
            roles.AddRange(claim.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        else if (claim.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in claim.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    roles.Add(item.GetString()!);
                }
            }
        }

        return roles;
    }

    /// <summary>Decodes base64url, adding the padding the encoder dropped.</summary>
    private static bool TryDecode(string value, out byte[] bytes)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padding = normalized.Length % 4;
        if (padding is 2 or 3)
        {
            normalized += new string('=', 4 - padding);
        }

        try
        {
            bytes = Convert.FromBase64String(normalized);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
