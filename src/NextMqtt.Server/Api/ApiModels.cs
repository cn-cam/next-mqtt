namespace NextMqtt.Server.Api;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, string Username);

public sealed record PublishRequest(
    string Topic,
    string Payload,
    string? Encoding = "plain",
    int QoS = 0,
    bool Retain = false);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record ApiError(string Code, string Message);

public sealed record VersionInfo(string Name, string Version, string Runtime, string Environment);
