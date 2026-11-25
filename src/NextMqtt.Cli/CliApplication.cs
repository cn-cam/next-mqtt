using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Text;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Cli;

/// <summary>Connection settings shared by every CLI command.</summary>
/// <remarks>只保存命令行解析后的结果；真正的连接、超时与 TLS 握手在 <see cref="MqttCliClient"/> 里完成。</remarks>
internal sealed record CliClientOptions
{
    /// <summary>Broker host name or address.</summary>
    /// <remarks>容器或远端场景要显式给值；不写就是 localhost。</remarks>
    public string Host { get; init; } = "localhost";

    /// <summary>Broker port; 1883 plain, 8883 with TLS unless overridden.</summary>
    /// <remarks>显式 --port 优先于 TLS 的默认端口。</remarks>
    public int Port { get; init; } = 1883;

    /// <summary>True to wrap the connection in TLS.</summary>
    /// <remarks>只改传输层，MQTT 报文内容不变。</remarks>
    public bool UseTls { get; init; }

    /// <summary>True to skip certificate validation; testing only.</summary>
    /// <remarks>为自签证书准备的开关，生产环境不要用。</remarks>
    public bool Insecure { get; init; }

    /// <summary>Client identifier, or null to generate one per run.</summary>
    /// <remarks>留空时每次运行都是新客户端，不会续上之前的会话。</remarks>
    public string? ClientId { get; init; }

    /// <summary>Username sent in CONNECT.</summary>
    /// <remarks>为空时不发用户名，是否允许匿名由服务端决定。</remarks>
    public string? Username { get; init; }

    /// <summary>Password sent in CONNECT; UTF-8 encoded.</summary>
    /// <remarks>空字符串等同于不发送密码。</remarks>
    public string? Password { get; init; }

    /// <summary>Protocol level requested in CONNECT.</summary>
    /// <remarks>3.1.1 不会写 MQTT 5 属性；request 命令要求 5.0。</remarks>
    public MqttProtocolVersion Version { get; init; } = MqttProtocolVersion.V500;

    /// <summary>Clean start flag; false resumes an existing session.</summary>
    /// <remarks>--resume 会把它置为 false，前提是服务端还留着同名会话。</remarks>
    public bool CleanStart { get; init; } = true;

    /// <summary>MQTT 5 session expiry in seconds; 0 ends the session with the connection.</summary>
    /// <remarks>只有 5.0 才会写进 CONNECT 属性。</remarks>
    public uint SessionExpirySeconds { get; init; }

    /// <summary>Keep-alive interval advertised in CONNECT.</summary>
    /// <remarks>固定 60 秒；长跑订阅脚本可按服务端超时调整。</remarks>
    public ushort KeepAliveSeconds { get; init; } = 60;
}

/// <summary>Small MQTT 5 / 3.1.1 client used by the CLI commands.</summary>
/// <remarks>同步用法、单连接、够用即可；不做重连、会话恢复与集群发现。</remarks>
internal sealed class MqttCliClient : IAsyncDisposable
{
    /// <summary>Socket kept so disposal can close it after the stream.</summary>
    private readonly TcpClient _tcp;
    /// <summary>Network or TLS stream carrying MQTT frames.</summary>
    private readonly Stream _stream;
    /// <summary>Frame reader over the stream.</summary>
    private readonly MqttPacketReader _reader;
    /// <summary>Serialises writes so two packets never interleave.</summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    /// <summary>Packets read while waiting for a specific type.</summary>
    private readonly Queue<MqttPacket> _pending = new();
    /// <summary>Protocol level chosen at connect time.</summary>
    private readonly MqttProtocolVersion _version;
    /// <summary>Last packet identifier used; the next one wraps from 65535 to 1.</summary>
    private ushort _nextPacketId;

    /// <summary>Creates a client around an established stream.</summary>
    /// <remarks>读路径直接复用流，写路径串行化；构造时不发送任何报文。</remarks>
    /// <param name="tcp">Connected socket, kept for disposal and logging.</param>
    /// <param name="stream">Network or TLS stream carrying MQTT.</param>
    /// <param name="version">Protocol level used for encoding.</param>
    private MqttCliClient(TcpClient tcp, Stream stream, MqttProtocolVersion version)
    {
        _tcp = tcp;
        _stream = stream;
        _version = version;
        _reader = new MqttPacketReader(System.IO.Pipelines.PipeReader.Create(stream));
    }

    /// <summary>CONNACK received during the handshake.</summary>
    /// <remarks>只在连接成功后可用；reason code 非零的连接会直接抛出，不会返回实例。</remarks>
    public ConnAckPacket ConnAck { get; private set; } = new();

    /// <summary>Opens a TCP or TLS connection and completes the MQTT handshake.</summary>
    /// <remarks>15 秒总超时；生成客户端 ID，发送 CONNECT 并等待 CONNACK，非 0x00 视为失败。</remarks>
    /// <param name="options">Host, credentials and session settings.</param>
    /// <param name="cancellationToken">Cancels the handshake.</param>
    /// <returns>A connected client that the caller must dispose.</returns>
    public static async Task<MqttCliClient> ConnectAsync(CliClientOptions options, CancellationToken cancellationToken)
    {
        var tcp = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        await tcp.ConnectAsync(options.Host, options.Port, timeout.Token);

        Stream stream = tcp.GetStream();

        if (options.UseTls)
        {
            var ssl = new SslStream(
                stream,
                leaveInnerStreamOpen: false,
                (_, _, _, errors) => options.Insecure || errors == SslPolicyErrors.None);

            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = options.Host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                },
                timeout.Token);

            stream = ssl;
        }

        var client = new MqttCliClient(tcp, stream, options.Version);
        var clientId = options.ClientId ?? $"nextmqtt-cli-{Guid.NewGuid():N}"[..24];
        var properties = new MqttProperties();

        if (options.Version == MqttProtocolVersion.V500 && options.SessionExpirySeconds > 0)
        {
            properties.AddUInt32(MqttPropertyId.SessionExpiryInterval, options.SessionExpirySeconds);
        }

        await client.SendAsync(new ConnectPacket
        {
            ProtocolVersion = options.Version,
            ClientId = clientId,
            CleanStart = options.CleanStart,
            KeepAliveSeconds = options.KeepAliveSeconds,
            Properties = properties,
            Username = options.Username,
            Password = string.IsNullOrEmpty(options.Password) ? null : Encoding.UTF8.GetBytes(options.Password),
        }, timeout.Token);

        var response = await client.ReadPacketAsync(timeout.Token);

        if (response is not ConnAckPacket connAck)
        {
            throw new InvalidOperationException($"Expected CONNACK, received {response?.Type.ToString() ?? "nothing"}.");
        }

        if (connAck.ReasonCode != MqttReasonCodes.Success)
        {
            throw new InvalidOperationException(
                $"Broker rejected the connection (reason 0x{connAck.ReasonCode:X2}).");
        }

        client.ConnAck = connAck;
        return client;
    }

    /// <summary>Encodes and writes one packet under the write lock.</summary>
    /// <remarks>先完整编码到缓冲区再一次性写出，避免并发写入把两个报文交织在一起。</remarks>
    /// <param name="packet">Packet to send.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task SendAsync(MqttPacket packet, CancellationToken cancellationToken)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);

        await _writeGate.WaitAsync(cancellationToken);

        try
        {
            MqttPacketWriter.Write(buffer, packet, _version);
            await _stream.WriteAsync(buffer.WrittenMemory, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Reads the next packet, taking buffered ones first.</summary>
    /// <remarks>返回 null 表示 Broker 已关闭连接。</remarks>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<MqttPacket?> ReadPacketAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count > 0)
        {
            return _pending.Dequeue();
        }

        return await _reader.ReadAsync(_version, cancellationToken);
    }

    /// <summary>Reads until a packet of the requested type arrives.</summary>
    /// <remarks>其他类型的报文放进待处理队列而不是丢弃，恢复会话时 PUBLISH 可能早于 SUBACK 到达。</remarks>
    /// <typeparam name="T">Packet type to wait for.</typeparam>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The first packet of type <typeparamref name="T"/>.</returns>
    public async Task<T> ReadUntilAsync<T>(CancellationToken cancellationToken)
        where T : MqttPacket
    {
        while (true)
        {
            // Read from the wire so buffered packets are not returned twice.
            var packet = await _reader.ReadAsync(_version, cancellationToken)
                ?? throw new IOException("Connection closed by the broker.");

            if (packet is T typed)
            {
                return typed;
            }

            // A resumed session can receive PUBLISH packets before SUBACK;
            // keep them for the caller instead of dropping them.
            _pending.Enqueue(packet);
        }
    }

    /// <summary>Subscribes to every filter with one SUBSCRIBE and waits for SUBACK.</summary>
    /// <remarks>任何一个过滤器被拒（原因码 ≥ 0x80）都按失败处理，便于脚本立刻发现权限问题。</remarks>
    /// <param name="filters">Topic filters to subscribe to.</param>
    /// <param name="qos">Requested QoS for all filters.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    public async Task SubscribeAsync(IEnumerable<string> filters, MqttQoS qos, CancellationToken cancellationToken)
    {
        var packetId = NextPacketId();

        await SendAsync(new SubscribePacket
        {
            PacketId = packetId,
            Subscriptions = filters
                .Select(filter => new MqttSubscriptionRequest(filter, new MqttSubscriptionOptions(qos)))
                .ToList(),
        }, cancellationToken);

        var subAck = await ReadUntilAsync<SubAckPacket>(cancellationToken);

        if (subAck.ReasonCodes.Any(code => code >= 0x80))
        {
            throw new InvalidOperationException(
                $"Subscription rejected: {string.Join(", ", subAck.ReasonCodes.Select(code => $"0x{code:X2}"))}.");
        }
    }

    /// <summary>Publishes and completes the QoS handshake for the requested level.</summary>
    /// <remarks>QoS 0 不分配包标识；QoS 1 等 PUBACK，QoS 2 走完 PUBREC/PUBREL/PUBCOMP 才返回。</remarks>
    /// <param name="topic">Destination topic.</param>
    /// <param name="payload">Payload bytes.</param>
    /// <param name="qos">Delivery guarantee.</param>
    /// <param name="retain">Store the message as retained.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The packet identifier used, or zero for QoS 0.</returns>
    public async Task<ushort> PublishAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        MqttQoS qos,
        bool retain,
        CancellationToken cancellationToken)
    {
        var packetId = qos == MqttQoS.AtMostOnce ? (ushort)0 : NextPacketId();

        await SendAsync(new PublishPacket
        {
            Topic = topic,
            Payload = payload.ToArray(),
            QoS = qos,
            Retain = retain,
            PacketId = packetId,
        }, cancellationToken);

        if (qos == MqttQoS.AtLeastOnce)
        {
            var ack = await ReadUntilAsync<PubAckPacket>(cancellationToken);

            if (ack.ReasonCode >= 0x80)
            {
                throw new InvalidOperationException($"Publish rejected with reason 0x{ack.ReasonCode:X2}.");
            }
        }
        else if (qos == MqttQoS.ExactlyOnce)
        {
            var rec = await ReadUntilAsync<PubRecPacket>(cancellationToken);

            if (rec.ReasonCode >= 0x80)
            {
                throw new InvalidOperationException($"Publish rejected with reason 0x{rec.ReasonCode:X2}.");
            }

            await SendAsync(new PubRelPacket { PacketId = packetId }, cancellationToken);
            await ReadUntilAsync<PubCompPacket>(cancellationToken);
        }

        return packetId;
    }

    /// <summary>Acknowledges an inbound PUBLISH according to its QoS.</summary>
    /// <remarks>QoS 2 会等待 PUBREL 后再回 PUBCOMP；QoS 0 不需要任何回应。</remarks>
    /// <param name="publish">Inbound message to acknowledge.</param>
    /// <param name="cancellationToken">Cancels the handshake.</param>
    public async Task AcknowledgeAsync(PublishPacket publish, CancellationToken cancellationToken)
    {
        switch (publish.QoS)
        {
            case MqttQoS.AtLeastOnce:
                await SendAsync(new PubAckPacket { PacketId = publish.PacketId }, cancellationToken);
                break;
            case MqttQoS.ExactlyOnce:
                await SendAsync(new PubRecPacket { PacketId = publish.PacketId }, cancellationToken);
                var pubRel = await ReadUntilAsync<PubRelPacket>(cancellationToken);
                await SendAsync(new PubCompPacket { PacketId = pubRel.PacketId }, cancellationToken);
                break;
        }
    }

    /// <summary>Sends DISCONNECT on a best-effort basis.</summary>
    /// <remarks>套接字已断开时静默忽略：这和正常退出没有区别。</remarks>
    /// <param name="cancellationToken">Cancels the send.</param>
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(new DisconnectPacket(), cancellationToken);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    /// <summary>Returns the next packet identifier, skipping zero.</summary>
    /// <remarks>到达 65535 后回到 1；单连接内不会复用仍在飞行中的标识。</remarks>
    private ushort NextPacketId()
    {
        _nextPacketId = _nextPacketId == ushort.MaxValue ? (ushort)1 : (ushort)(_nextPacketId + 1);
        return _nextPacketId;
    }

    /// <summary>Waits for any in-flight write, then closes the stream and socket.</summary>
    /// <remarks>先获取写锁再释放信号量，保证最后一条报文写完；重复释放安全。</remarks>
    public async ValueTask DisposeAsync()
    {
        await _writeGate.WaitAsync();
        _writeGate.Dispose();
        _stream.Dispose();
        _tcp.Dispose();
    }
}

/// <summary>Command dispatch and the per-command implementations.</summary>
/// <remarks>参数用 <c>--name value</c> 或 <c>--name=value</c>；不认识的选项会被忽略而不是报错。</remarks>
internal static class CliApplication
{
    /// <summary>Parses the command and runs it, mapping failures to exit codes.</summary>
    /// <remarks>0 成功、1 运行失败、2 参数错误、130 被 Ctrl+C 中断，便于脚本判断。</remarks>
    /// <param name="args">Raw command line arguments.</param>
    /// <returns>Process exit code.</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "version" or "--version" => PrintVersion(),
                "pub" or "publish" => await PublishAsync(args[1..]),
                "sub" or "subscribe" => await SubscribeAsync(args[1..]),
                "request" or "req" => await RequestAsync(args[1..]),
                "bench" or "benchmark" => await BenchmarkAsync(args[1..]),
                "bench-connections" or "connect-bench" => await BenchmarkConnectionsAsync(args[1..]),
                "hash" or "hash-password" => HashPassword(args[1..]),
                _ => Unknown(args[0]),
            };
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Prints the version baked into the assembly.</summary>
    /// <remarks>版本号由构建属性写入，不在源码里写死，避免和发布版本脱节。</remarks>
    private static int PrintVersion()
    {
        var assembly = typeof(CliApplication).Assembly;
        var version = assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        var revision = version.IndexOf('+');
        if (revision >= 0)
        {
            version = version[..revision];
        }

        Console.WriteLine($"nextmqtt {version}");
        return 0;
    }

    /// <summary>Reports an unknown command and prints the help text.</summary>
    /// <remarks>返回 2，和缺少参数保持一致。</remarks>
    /// <param name="command">Command that was not recognised.</param>
    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'");
        PrintHelp();
        return 2;
    }

    /// <summary>Hashes a password with the broker's PBKDF2 format.</summary>
    /// <remarks>密码可以来自 <c>--password</c> 或标准输入；输出一行，可直接写进配置或环境变量。</remarks>
    /// <param name="args">Command arguments.</param>
    /// <returns>Exit code.</returns>
    private static int HashPassword(string[] args)
    {
        var options = ParseOptions(args);
        var password = options.GetValueOrDefault("password");

        if (string.IsNullOrEmpty(password))
        {
            Console.Error.Write("Password: ");
            password = Console.ReadLine();
        }

        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("error: a password is required (--password or stdin)");
            return 2;
        }

        Console.WriteLine(NextMqtt.Broker.Security.PasswordHasher.Hash(password));
        return 0;
    }

    /// <summary>Publishes one message from a literal, a file or standard input.</summary>
    /// <remarks>三种载荷来源按 file、message、stdin 的优先级取第一个；都没有则发送空载荷。</remarks>
    /// <param name="args">Command arguments.</param>
    /// <returns>Exit code.</returns>
    private static async Task<int> PublishAsync(string[] args)
    {
        var options = ParseOptions(args);

        if (!options.TryGetValue("topic", out var topic) || string.IsNullOrEmpty(topic))
        {
            Console.Error.WriteLine("error: --topic is required");
            return 2;
        }

        var payload = options.TryGetValue("file", out var file) && !string.IsNullOrEmpty(file)
            ? await File.ReadAllBytesAsync(file)
            : options.TryGetValue("message", out var message)
                ? Encoding.UTF8.GetBytes(message ?? string.Empty)
                : options.ContainsKey("stdin")
                    ? Encoding.UTF8.GetBytes(await Console.In.ReadToEndAsync())
                    : [];

        var qos = ParseQos(options.GetValueOrDefault("qos"));
        var retain = options.ContainsKey("retain");

        await using var client = await MqttCliClient.ConnectAsync(BuildClientOptions(options), CancellationToken.None);

        await client.PublishAsync(topic, payload, qos, retain, CancellationToken.None);
        await client.DisconnectAsync(CancellationToken.None);

        Console.WriteLine(
            $"published {payload.Length} byte(s) to {topic} (qos={qos}, retain={retain.ToString().ToLowerInvariant()})");

        return 0;
    }

    /// <summary>Sends a request and waits for the correlated reply.</summary>
    /// <remarks>
    /// 使用 MQTT 5 的 Response Topic 与 Correlation Data：先订阅回复主题，再发布请求，
    /// 收到 Correlation Data 匹配的第一条消息即返回。
    /// </remarks>
    private static async Task<int> RequestAsync(string[] args)
    {
        var options = ParseOptions(args);
        var topic = options.GetValueOrDefault("topic");

        if (string.IsNullOrWhiteSpace(topic))
        {
            Console.Error.WriteLine("error: --topic is required");
            return 2;
        }

        if (ParseProtocol(options.GetValueOrDefault("protocol")) != MqttProtocolVersion.V500)
        {
            Console.Error.WriteLine("error: request needs MQTT 5 (--protocol 5)");
            return 2;
        }

        var qos = ParseQos(options.GetValueOrDefault("qos"));
        var timeout = ParseInt(options.GetValueOrDefault("timeout"), 5, 1, 300);
        var clientOptions = BuildClientOptions(options);
        var responseTopic = options.GetValueOrDefault("response-topic");

        if (string.IsNullOrWhiteSpace(responseTopic))
        {
            responseTopic = $"$nextmqtt/reply/{clientOptions.ClientId ?? "cli"}/{Guid.NewGuid():N}";
        }

        var correlation = options.TryGetValue("correlation", out var correlationText)
            ? System.Text.Encoding.UTF8.GetBytes(correlationText)
            : Guid.NewGuid().ToByteArray();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

        try
        {
            await using var client = await MqttCliClient.ConnectAsync(clientOptions, cts.Token);
            await client.SubscribeAsync([responseTopic], qos, cts.Token);

            var payload = System.Text.Encoding.UTF8.GetBytes(
                options.GetValueOrDefault("payload") ?? options.GetValueOrDefault("message") ?? string.Empty);

            var properties = new MqttProperties()
                .AddString(MqttPropertyId.ResponseTopic, responseTopic)
                .AddBinary(MqttPropertyId.CorrelationData, correlation);

            await client.SendAsync(new PublishPacket
            {
                Topic = topic,
                Payload = payload,
                QoS = qos,
                PacketId = qos == MqttQoS.AtMostOnce ? (ushort)0 : (ushort)1,
                Properties = properties,
            }, cts.Token);

            Console.WriteLine($"request sent to {topic}; waiting up to {timeout}s on {responseTopic}");

            while (true)
            {
                var packet = await client.ReadPacketAsync(cts.Token);

                if (packet is not PublishPacket reply)
                {
                    continue;
                }

                var received = reply.Properties.GetBinary(MqttPropertyId.CorrelationData);

                if (received is not null && !received.AsSpan().SequenceEqual(correlation))
                {
                    continue;
                }

                var text = System.Text.Encoding.UTF8.GetString(reply.Payload);
                Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] {reply.Topic} qos={(byte)reply.QoS} bytes={reply.Payload.Length}");
                Console.WriteLine(text);
                return 0;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"error: no reply within {timeout}s");
            return 1;
        }
    }

    /// <summary>Subscribes and prints messages until the count is reached or Ctrl+C.</summary>
    /// <remarks>每次收到 PUBLISH 都按 QoS 回执；<c>--count</c> 为 0 表示一直运行。</remarks>
    /// <param name="args">Command arguments.</param>
    /// <returns>Exit code.</returns>
    private static async Task<int> SubscribeAsync(string[] args)
    {
        var options = ParseOptions(args);
        var topics = options.TryGetValue("topic", out var topic) && !string.IsNullOrEmpty(topic)
            ? topic.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        if (topics.Length == 0)
        {
            Console.Error.WriteLine("error: --topic is required (comma-separated filters are allowed)");
            return 2;
        }

        var qos = ParseQos(options.GetValueOrDefault("qos"));
        var count = options.TryGetValue("count", out var countValue) && int.TryParse(countValue, out var parsed)
            ? parsed
            : 0;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        await using var client = await MqttCliClient.ConnectAsync(BuildClientOptions(options), cts.Token);
        await client.SubscribeAsync(topics, qos, cts.Token);

        Console.WriteLine(
            $"subscribed to {string.Join(", ", topics)} (qos={qos}); waiting for messages{(count > 0 ? $" ({count})" : "")}");

        var received = 0;

        while (!cts.IsCancellationRequested)
        {
            var packet = await client.ReadPacketAsync(cts.Token);

            if (packet is null)
            {
                break;
            }

            if (packet is PublishPacket publish)
            {
                Console.WriteLine(
                    $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {publish.Topic} qos={(int)publish.QoS} retain={publish.Retain.ToString().ToLowerInvariant()} bytes={publish.Payload.Length} {FormatPayload(publish.Payload)}");

                await client.AcknowledgeAsync(publish, cts.Token);
                received++;

                if (count > 0 && received >= count)
                {
                    break;
                }
            }
        }

        await client.DisconnectAsync(CancellationToken.None);
        return 0;
    }

    /// <summary>Opens many connections at once to measure handshake throughput.</summary>
    /// <remarks>并发建连，统计成功数与每秒连接数，可再保持一段时间观察服务端连接数。</remarks>
    /// <param name="args">Command arguments.</param>
    /// <returns>0 全部成功，1 有失败。</returns>
    private static async Task<int> BenchmarkConnectionsAsync(string[] args)
    {
        var options = ParseOptions(args);
        var count = ParseInt(options.GetValueOrDefault("connections"), 1000, 1, 100_000);
        var holdSeconds = ParseInt(options.GetValueOrDefault("hold"), 5, 0, 300);
        var clients = new ConcurrentBag<MqttCliClient>();
        var failures = 0;

        Console.WriteLine($"connection benchmark: {count} client(s), hold {holdSeconds}s");

        var stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, count).Select(async index =>
        {
            var clientOptions = BuildClientOptions(options) with
            {
                ClientId = $"{options.GetValueOrDefault("client-id") ?? "nextmqtt-conn"}-{index}",
            };

            try
            {
                var client = await MqttCliClient.ConnectAsync(clientOptions, CancellationToken.None);
                clients.Add(client);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failures);
                if (failures <= 5)
                {
                    Console.Error.WriteLine($"client {index}: {ex.Message}");
                }
            }
        }));

        stopwatch.Stop();
        var connected = count - failures;
        var rate = connected / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);

        Console.WriteLine(
            $"connected {connected:N0}/{count:N0} in {stopwatch.Elapsed.TotalSeconds:F2}s · {rate:N0} conn/s · failures={failures}");

        if (holdSeconds > 0)
        {
            Console.WriteLine($"holding {connected:N0} connection(s) for {holdSeconds}s...");
            await Task.Delay(TimeSpan.FromSeconds(holdSeconds));
        }

        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }

        return failures > 0 ? 1 : 0;
    }

    /// <summary>Publishes a fixed payload many times from several clients.</summary>
    /// <remarks>每个客户端发布到自己的一级子主题，避免订阅端成为瓶颈；吞吐按总消息数计算。</remarks>
    /// <param name="args">Command arguments.</param>
    /// <returns>0 全部完成，1 有客户端失败。</returns>
    private static async Task<int> BenchmarkAsync(string[] args)
    {
        var options = ParseOptions(args);
        var clients = ParseInt(options.GetValueOrDefault("clients"), 10, 1, 10_000);
        var messages = ParseInt(options.GetValueOrDefault("messages"), 1000, 1, 10_000_000);
        var size = ParseInt(options.GetValueOrDefault("size"), 64, 0, 1024 * 1024);
        var qos = ParseQos(options.GetValueOrDefault("qos"));
        var topic = options.GetValueOrDefault("topic") ?? "bench";
        var payload = new byte[size];
        Array.Fill(payload, (byte)'x');

        Console.WriteLine(
            $"benchmark: {clients} client(s) x {messages} message(s) x {size} bytes, qos={qos}");

        var stopwatch = Stopwatch.StartNew();
        var failures = 0;
        var completed = 0;

        var tasks = Enumerable.Range(0, clients).Select(async index =>
        {
            var clientOptions = BuildClientOptions(options) with
            {
                ClientId = $"{options.GetValueOrDefault("client-id") ?? "nextmqtt-bench"}-{index}",
            };

            try
            {
                await using var client = await MqttCliClient.ConnectAsync(clientOptions, CancellationToken.None);

                for (var i = 0; i < messages; i++)
                {
                    await client.PublishAsync($"{topic}/{index}", payload, qos, retain: false, CancellationToken.None);
                    Interlocked.Increment(ref completed);
                }

                await client.DisconnectAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failures);
                Console.Error.WriteLine($"client {index}: {ex.Message}");
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        var total = (long)clients * messages;
        var seconds = stopwatch.Elapsed.TotalSeconds;
        var rate = total / Math.Max(seconds, 0.001);

        Console.WriteLine(
            $"completed {completed:N0}/{total:N0} in {seconds:F2}s · {rate:N0} msg/s · {rate * size / 1024 / 1024:F2} MiB/s · failures={failures}");

        return failures > 0 ? 1 : 0;
    }

    /// <summary>Turns parsed options into connection settings.</summary>
    /// <remarks><c>--tls</c> 时端口默认 8883；<c>--resume</c> 关闭 clean start。</remarks>
    /// <param name="options">Parsed command-line options.</param>
    private static CliClientOptions BuildClientOptions(Dictionary<string, string?> options)
    {
        var tls = options.ContainsKey("tls");
        var port = options.TryGetValue("port", out var portValue) && int.TryParse(portValue, out var parsedPort)
            ? parsedPort
            : tls
                ? 8883
                : 1883;

        return new CliClientOptions
        {
            Host = options.GetValueOrDefault("host") ?? "localhost",
            Port = port,
            UseTls = tls,
            Insecure = options.ContainsKey("insecure"),
            ClientId = options.GetValueOrDefault("client-id"),
            Username = options.GetValueOrDefault("username"),
            Password = options.GetValueOrDefault("password"),
            Version = ParseProtocol(options.GetValueOrDefault("protocol")),
            CleanStart = !options.ContainsKey("resume"),
            SessionExpirySeconds = (uint)ParseInt(options.GetValueOrDefault("session-expiry"), 0, 0, int.MaxValue),
        };
    }

    /// <summary>Parses the protocol version, defaulting to MQTT 5.</summary>
    /// <remarks>接受 5、5.0、v5 与 3、3.1.1、v3、v311；其他取值直接报错而不是回退。</remarks>
    /// <param name="value">Raw option value.</param>
    private static MqttProtocolVersion ParseProtocol(string? value) => value switch
    {
        null or "" or "5" or "5.0" or "v5" => MqttProtocolVersion.V500,
        "3" or "3.1.1" or "v3" or "v311" => MqttProtocolVersion.V311,
        _ => throw new ArgumentException($"Unsupported protocol '{value}'. Use 5 or 3.1.1."),
    };

    /// <summary>Parses the QoS level, defaulting to 0.</summary>
    /// <remarks>只接受 0、1、2；其他取值报错，避免把笔误当成 QoS 0 静默发布。</remarks>
    /// <param name="value">Raw option value.</param>
    private static MqttQoS ParseQos(string? value) => value switch
    {
        null or "" or "0" => MqttQoS.AtMostOnce,
        "1" => MqttQoS.AtLeastOnce,
        "2" => MqttQoS.ExactlyOnce,
        _ => throw new ArgumentException($"Invalid QoS '{value}'."),
    };

    /// <summary>Parses an integer option and clamps it into the allowed range.</summary>
    /// <remarks>缺省时返回 fallback；非数字直接报错，越界则静默夹到边界。</remarks>
    /// <param name="value">Raw option value.</param>
    /// <param name="fallback">Value to use when the option is absent.</param>
    /// <param name="min">Inclusive lower bound.</param>
    /// <param name="max">Inclusive upper bound.</param>
    private static int ParseInt(string? value, int fallback, int min, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"'{value}' is not a number.");
        }

        return Math.Clamp(parsed, min, max);
    }

    /// <summary>Splits arguments into a case-insensitive option map.</summary>
    /// <remarks>支持 <c>--k v</c>、<c>--k=v</c> 与裸开关；没有值的开关记为 "true"。位置参数被忽略。</remarks>
    /// <param name="args">Command arguments without the command name.</param>
    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = argument[2..];
            var separator = name.IndexOf('=');

            if (separator > 0)
            {
                options[name[..separator]] = name[(separator + 1)..];
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[name] = args[++i];
            }
            else
            {
                options[name] = "true";
            }
        }

        return options;
    }

    /// <summary>Renders a payload for the console, falling back to Base64.</summary>
    /// <remarks>不是有效 UTF-8 或含有控制字符时输出 Base64；文本超过 512 字符会被截断。</remarks>
    /// <param name="payload">Received payload bytes.</param>
    private static string FormatPayload(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return "(empty)";
        }

        try
        {
            var text = new UTF8Encoding(false, true).GetString(payload);

            if (text.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
            {
                return Convert.ToBase64String(payload);
            }

            return text.Length > 512 ? $"{text[..512]}…" : text;
        }
        catch (DecoderFallbackException)
        {
            return Convert.ToBase64String(payload);
        }
    }

    /// <summary>Prints usage, connection options and a few examples.</summary>
    /// <remarks>帮助文本是用户唯一的离线文档，改命令行参数时要同步更新这里。</remarks>
    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            nextmqtt - MQTT command line client for NextMQTT

            Usage:
              nextmqtt pub   --topic <topic> [--message <text> | --file <path> | --stdin]
                             [--qos 0|1|2] [--retain] [connection options]
              nextmqtt sub   --topic <filter>[,<filter>] [--qos 0|1|2] [--count N]
                             [connection options]
              nextmqtt bench [--clients 10] [--messages 1000] [--size 64] [--qos 0]
                             [--topic bench] [connection options]
              nextmqtt bench-connections [--connections 1000] [--hold 5]
                             [connection options]
              nextmqtt request --topic <topic> [--payload <text>] [--response-topic <topic>]
                             [--correlation <text>] [--timeout 5] [--qos 0] [connection options]
              nextmqtt hash-password [--password <secret>]
              nextmqtt version

            Connection options:
              --host <host>          Broker host (default: localhost)
              --port <port>          Broker port (default: 1883, or 8883 with --tls)
              --tls                 Use TLS
              --insecure            Skip TLS certificate validation (testing only)
              --username <name>
              --password <secret>
              --client-id <id>
              --protocol <5|3.1.1>  MQTT protocol version (default: 5)
              --session-expiry <s>  MQTT 5 session expiry interval
              --resume              Resume a session (disable clean start)

            Examples:
              nextmqtt pub -h localhost -p 1883 --topic demo/hello --message hello
              nextmqtt sub --topic 'sensors/#' --qos 1 --count 5
              nextmqtt bench --clients 50 --messages 2000 --size 128 --qos 1
            """);
    }
}
