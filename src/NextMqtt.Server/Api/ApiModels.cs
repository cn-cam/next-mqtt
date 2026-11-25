namespace NextMqtt.Server.Api;

/// <summary>Dashboard login form.</summary>
/// <param name="Username">Admin username.</param>
/// <param name="Password">Plaintext password; compared against the stored hash.</param>
public sealed record LoginRequest(string Username, string Password);

/// <summary>Bearer token issued by the login endpoint.</summary>
/// <param name="Token">HS256 token to send as <c>Authorization: Bearer</c>.</param>
/// <param name="ExpiresAt">Expiry of the token, UTC.</param>
/// <param name="Username">Account the token belongs to.</param>
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, string Username);

/// <summary>Body of the administrative publish endpoint.</summary>
/// <param name="Topic">Topic name to publish to.</param>
/// <param name="Payload">Payload as text; interpretation depends on <paramref name="Encoding"/>.</param>
/// <param name="Encoding">plain, base64 or hex.</param>
/// <param name="QoS">Delivery QoS (0-2).</param>
/// <param name="Retain">Store the message as retained.</param>
public sealed record PublishRequest(
    string Topic,
    string Payload,
    string? Encoding = "plain",
    int QoS = 0,
    bool Retain = false);

/// <summary>Paged list returned by table endpoints.</summary>
/// <typeparam name="T">Row type.</typeparam>
/// <param name="Items">Rows for the requested page.</param>
/// <param name="Total">Total rows before paging.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Machine-readable error body.</summary>
/// <param name="Code">Stable error code, for example <c>unauthorized</c>.</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record ApiError(string Code, string Message);

/// <summary>Build and runtime information shown on the settings page.</summary>
/// <param name="Name">Product name.</param>
/// <param name="Version">Assembly informational version.</param>
/// <param name="Runtime">.NET runtime description.</param>
/// <param name="Environment">ASP.NET environment name.</param>
public sealed record VersionInfo(string Name, string Version, string Runtime, string Environment);
