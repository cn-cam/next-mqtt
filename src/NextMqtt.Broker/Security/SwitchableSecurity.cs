namespace NextMqtt.Broker.Security;

/// <summary>Authenticator whose implementation can be replaced at runtime.</summary>
/// <remarks>
/// 插件重载时只交换引用：正在进行的认证仍走旧实例，直到调用返回，
/// 不会出现“半截换掉”的状态。实现必须无状态或自行保证并发安全。
/// </remarks>
public sealed class SwitchableAuthenticator : IClientAuthenticator
{
    /// <summary>Serialises swaps so two reloads cannot interleave.</summary>
    private readonly object _gate = new();

    /// <summary>Authenticator used for new calls.</summary>
    private IClientAuthenticator _inner;

    /// <summary>Number of completed swaps.</summary>
    private long _generation;

    /// <summary>Wraps the initial authenticator.</summary>
    /// <remarks>启动时传入内置实现；插件加载后由重载流程替换。</remarks>
    /// <param name="inner">Authenticator used before the first swap.</param>
    public SwitchableAuthenticator(IClientAuthenticator inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Number of swaps performed, useful for diagnostics.</summary>
    /// <remarks>只在成功后递增，用于确认重载真的发生了。</remarks>
    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>Replaces the authenticator used for subsequent connections.</summary>
    /// <remarks>不等待在途请求；旧实例由调用方在确认安全后释放。</remarks>
    /// <param name="inner">New authenticator.</param>
    public void Swap(IClientAuthenticator inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        lock (_gate)
        {
            _inner = inner;
        }

        Interlocked.Increment(ref _generation);
    }

    /// <summary>Delegates to the current authenticator.</summary>
    /// <remarks>读取一次引用后调用，避免交换过程中读到一半的状态。</remarks>
    /// <param name="context">Identity and credentials from CONNECT.</param>
    /// <param name="cancellationToken">Cancels remote lookups.</param>
    public ValueTask<AuthenticationResult> AuthenticateAsync(
        ClientAuthenticationContext context,
        CancellationToken cancellationToken = default)
        => Volatile.Read(ref _inner).AuthenticateAsync(context, cancellationToken);
}

/// <summary>Topic authorizer whose implementation can be replaced at runtime.</summary>
/// <remarks>与认证器相同的交换语义：进行中的检查用旧规则完成。</remarks>
public sealed class SwitchableAuthorizer : ITopicAuthorizer
{
    /// <summary>Serialises swaps so two reloads cannot interleave.</summary>
    private readonly object _gate = new();

    /// <summary>Authorizer used for new checks.</summary>
    private ITopicAuthorizer _inner;

    /// <summary>Number of completed swaps.</summary>
    private long _generation;

    /// <summary>Wraps the initial authorizer.</summary>
    /// <remarks>启动时传入内置 ACL；插件加载后由重载流程替换。</remarks>
    /// <param name="inner">Authorizer used before the first swap.</param>
    public SwitchableAuthorizer(ITopicAuthorizer inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Number of swaps performed, useful for diagnostics.</summary>
    /// <remarks>与认证器的计数独立。</remarks>
    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>Replaces the authorizer used for subsequent checks.</summary>
    /// <remarks>进行中的检查不受影响，继续使用旧规则直到返回。</remarks>
    /// <param name="inner">New authorizer.</param>
    public void Swap(ITopicAuthorizer inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        lock (_gate)
        {
            _inner = inner;
        }

        Interlocked.Increment(ref _generation);
    }

    /// <summary>Delegates to the current authorizer.</summary>
    /// <remarks>只读一次引用，避免读到交换中的状态。</remarks>
    /// <param name="principal">Authenticated identity.</param>
    /// <param name="topic">Concrete topic name.</param>
    public bool CanPublish(ClientPrincipal principal, string topic)
        => Volatile.Read(ref _inner).CanPublish(principal, topic);

    /// <summary>Delegates to the current authorizer.</summary>
    /// <remarks>订阅检查包含过滤器覆盖判断，由具体实现决定。</remarks>
    /// <param name="principal">Authenticated identity.</param>
    /// <param name="filter">Requested filter.</param>
    public bool CanSubscribe(ClientPrincipal principal, string filter)
        => Volatile.Read(ref _inner).CanSubscribe(principal, filter);
}
