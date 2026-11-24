using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Security;

namespace NextMqtt.Broker.Tests;

public class SecurityTests
{
    [Fact]
    public void Password_hash_roundtrips_and_rejects_wrong_password()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple", iterations: 10_000);

        Assert.StartsWith("pbkdf2-sha256$", hash);
        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash));
        Assert.False(PasswordHasher.Verify("wrong", hash));
        Assert.False(PasswordHasher.Verify("", hash));
        Assert.False(PasswordHasher.Verify("x", "not-a-hash"));
    }

    [Fact]
    public async Task Local_user_authenticates()
    {
        var options = new AuthenticationOptions
        {
            Users =
            [
                new LocalUserOptions
                {
                    Username = "alice",
                    PasswordHash = PasswordHasher.Hash("s3cret", 10_000),
                    Roles = ["operator"],
                },
            ],
        };

        var authenticator = new ClientAuthenticator(options);
        var context = new ClientAuthenticationContext(
            "client-1", "alice", Encoding.UTF8.GetBytes("s3cret"), "test", Core.Protocol.MqttProtocolVersion.V500);

        var result = await authenticator.AuthenticateAsync(context);

        Assert.True(result.Succeeded);
        Assert.Equal("alice", result.Principal!.Username);
        Assert.Equal("client-1", result.Principal.ClientId);
        Assert.False(result.Principal.IsSuperuser);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var options = new AuthenticationOptions
        {
            Users =
            [
                new LocalUserOptions
                {
                    Username = "alice",
                    PasswordHash = PasswordHasher.Hash("s3cret", 10_000),
                },
            ],
        };

        var authenticator = new ClientAuthenticator(options);
        var context = new ClientAuthenticationContext(
            "client-1", "alice", Encoding.UTF8.GetBytes("wrong"), "test", Core.Protocol.MqttProtocolVersion.V500);

        var result = await authenticator.AuthenticateAsync(context);

        Assert.False(result.Succeeded);
        Assert.Equal(Core.Protocol.MqttReasonCodes.BadUserNameOrPassword, result.ReasonCode);
    }

    [Fact]
    public async Task Anonymous_access_can_be_enabled()
    {
        var authenticator = new ClientAuthenticator(new AuthenticationOptions { AllowAnonymous = true });
        var context = new ClientAuthenticationContext(
            "client-2", null, ReadOnlyMemory<byte>.Empty, "test", Core.Protocol.MqttProtocolVersion.V311);

        var result = await authenticator.AuthenticateAsync(context);

        Assert.True(result.Succeeded);
        Assert.Equal("client-2", result.Principal!.Name);
    }

    [Fact]
    public async Task Anonymous_is_rejected_when_not_enabled()
    {
        var authenticator = new ClientAuthenticator(new AuthenticationOptions { AllowAnonymous = false });
        var context = new ClientAuthenticationContext(
            "client-6", null, ReadOnlyMemory<byte>.Empty, "test", Core.Protocol.MqttProtocolVersion.V500);

        Assert.False((await authenticator.AuthenticateAsync(context)).Succeeded);
    }

    [Fact]
    public async Task Jwt_password_authenticates_and_carries_roles()
    {
        var secret = "unit-test-secret-with-enough-entropy";
        var token = CreateToken(secret, new
        {
            sub = "service-a",
            exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
            roles = new[] { "admin" },
        });

        var authenticator = new ClientAuthenticator(new AuthenticationOptions
        {
            Jwt = new JwtOptions { Secret = secret },
        });

        var context = new ClientAuthenticationContext(
            "client-3", "any", Encoding.UTF8.GetBytes(token), "test", Core.Protocol.MqttProtocolVersion.V500);

        var result = await authenticator.AuthenticateAsync(context);

        Assert.True(result.Succeeded);
        Assert.Equal("service-a", result.Principal!.Username);
        Assert.True(result.Principal.IsSuperuser);
        Assert.Equal("client-3", result.Principal.ClientId);
    }

    [Fact]
    public async Task Expired_jwt_is_rejected()
    {
        var secret = "unit-test-secret-with-enough-entropy";
        var token = CreateToken(secret, new
        {
            sub = "service-a",
            exp = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(),
        });

        var authenticator = new ClientAuthenticator(new AuthenticationOptions
        {
            Jwt = new JwtOptions { Secret = secret },
        });

        var context = new ClientAuthenticationContext(
            "client-4", "any", Encoding.UTF8.GetBytes(token), "test", Core.Protocol.MqttProtocolVersion.V500);

        Assert.False((await authenticator.AuthenticateAsync(context)).Succeeded);
    }

    [Fact]
    public async Task Tampered_jwt_is_rejected()
    {
        var secret = "unit-test-secret-with-enough-entropy";
        var token = CreateToken(secret, new
        {
            sub = "service-a",
            exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
        });

        var tampered = token[..^2] + (token[^2] == 'A' ? "BB" : "AA");

        var authenticator = new ClientAuthenticator(new AuthenticationOptions
        {
            Jwt = new JwtOptions { Secret = secret },
        });

        var context = new ClientAuthenticationContext(
            "client-5", "any", Encoding.UTF8.GetBytes(tampered), "test", Core.Protocol.MqttProtocolVersion.V500);

        Assert.False((await authenticator.AuthenticateAsync(context)).Succeeded);
    }

    [Fact]
    public void Acl_denies_by_default()
    {
        var authorizer = new TopicAuthorizer(new AclOptions());
        var user = ClientPrincipal.Create("alice", "c1", isSuperuser: false);

        Assert.False(authorizer.CanPublish(user, "a/b"));
        Assert.False(authorizer.CanSubscribe(user, "a/#"));
    }

    [Fact]
    public void Acl_rules_are_ordered_and_first_match_wins()
    {
        var authorizer = new TopicAuthorizer(new AclOptions
        {
            DefaultPolicy = AclDefaultPolicy.Deny,
            Rules =
            [
                new AclRuleOptions { Effect = AclEffect.Deny, Topic = "secret/#" },
                new AclRuleOptions { Effect = AclEffect.Allow, Topic = "#" },
            ],
        });

        var user = ClientPrincipal.Create("alice", "c1", isSuperuser: false);

        Assert.True(authorizer.CanPublish(user, "public/data"));
        Assert.False(authorizer.CanPublish(user, "secret/data"));
    }

    [Fact]
    public void Acl_placeholders_expand_username_and_client_id()
    {
        var authorizer = new TopicAuthorizer(new AclOptions
        {
            Rules =
            [
                new AclRuleOptions { Topic = "home/${username}/#", Action = AclAction.Both },
                new AclRuleOptions { Topic = "clients/${clientid}/#", Action = AclAction.Both },
            ],
        });

        var alice = ClientPrincipal.Create("alice", "device-1", isSuperuser: false);

        Assert.True(authorizer.CanPublish(alice, "home/alice/temperature"));
        Assert.False(authorizer.CanPublish(alice, "home/bob/temperature"));
        Assert.True(authorizer.CanPublish(alice, "clients/device-1/status"));
    }

    [Fact]
    public void Subscribe_acl_requires_the_rule_to_cover_the_requested_filter()
    {
        var authorizer = new TopicAuthorizer(new AclOptions
        {
            Rules = [new AclRuleOptions { Topic = "sensors/#" }],
        });

        var user = ClientPrincipal.Create("alice", "c1", isSuperuser: false);

        Assert.True(authorizer.CanSubscribe(user, "sensors/+/temperature"));
        Assert.True(authorizer.CanSubscribe(user, "sensors/#"));
        Assert.False(authorizer.CanSubscribe(user, "#"));
        Assert.False(authorizer.CanSubscribe(user, "other/#"));
    }

    [Fact]
    public void Superuser_bypasses_acl()
    {
        var authorizer = new TopicAuthorizer(new AclOptions { DefaultPolicy = AclDefaultPolicy.Deny });
        var admin = ClientPrincipal.Create("root", "c1", isSuperuser: true);

        Assert.True(authorizer.CanPublish(admin, "anything"));
        Assert.True(authorizer.CanSubscribe(admin, "#"));
    }

    [Fact]
    public void Wildcard_rule_does_not_cover_dollar_topics()
    {
        var authorizer = new TopicAuthorizer(new AclOptions
        {
            Rules = [new AclRuleOptions { Topic = "#" }],
        });

        var user = ClientPrincipal.Create("alice", "c1", isSuperuser: false);

        Assert.True(authorizer.CanSubscribe(user, "sensors/#"));
        Assert.False(authorizer.CanSubscribe(user, "$SYS/#"));
        Assert.True(authorizer.CanPublish(user, "sensors/a"));
        Assert.False(authorizer.CanPublish(user, "$SYS/load"));
    }

    [Fact]
    public void Shared_subscription_acl_uses_the_effective_filter()
    {
        var authorizer = new TopicAuthorizer(new AclOptions
        {
            Rules = [new AclRuleOptions { Topic = "jobs/#" }],
        });

        var user = ClientPrincipal.Create("alice", "c1", isSuperuser: false);

        Assert.True(authorizer.CanSubscribe(user, "$share/workers/jobs/ready"));
        Assert.False(authorizer.CanSubscribe(user, "$share/workers/other/ready"));
    }

    private static string CreateToken(string secret, object payload)
    {
        static string Encode(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = Encode(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var body = Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = Encode(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.ASCII.GetBytes($"{header}.{body}")));

        return $"{header}.{body}.{signature}";
    }
}
