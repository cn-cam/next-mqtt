using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Cli;

internal sealed record CliClientOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 1883;
    public bool UseTls { get; init; }
    public bool Insecure { get; init; }
    public string? ClientId { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public MqttProtocolVersion Version { get; init; } = MqttProtocolVersion.V500;
    public bool CleanStart { get; init; } = true;
    public uint SessionExpirySeconds { get; init; }
    public ushort KeepAliveSeconds { get; init; } = 60;
}

/// <summary>Small MQTT 5 / 3.1.1 client used by the CLI commands.</summary>
/// <remarks>同步用法、单连接、够用即可；不做重连、会话恢复与集群发现。</remarks>
internal sealed class MqttCliClient : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly Stream _stream;
    private readonly MqttPacketReader _reader;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Queue<MqttPacket> _pending = new();
    private readonly MqttProtocolVersion _version;
    private ushort _nextPacketId;

    private MqttCliClient(TcpClient tcp, Stream stream, MqttProtocolVersion version)
    {
        _tcp = tcp;
        _stream = stream;
        _version = version;
        _reader = new MqttPacketReader(System.IO.Pipelines.PipeReader.Create(stream));
    }

    public ConnAckPacket ConnAck { get; private set; } = new();

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

    public async Task<MqttPacket?> ReadPacketAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count > 0)
        {
            return _pending.Dequeue();
        }

        return await _reader.ReadAsync(_version, cancellationToken);
    }

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

    private ushort NextPacketId()
    {
        _nextPacketId = _nextPacketId == ushort.MaxValue ? (ushort)1 : (ushort)(_nextPacketId + 1);
        return _nextPacketId;
    }

    public async ValueTask DisposeAsync()
    {
        await _writeGate.WaitAsync();
        _writeGate.Dispose();
        _stream.Dispose();
        _tcp.Dispose();
    }
}

internal static class CliApplication
{
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

    private static int PrintVersion()
    {
        Console.WriteLine("nextmqtt 0.1.2");
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'");
        PrintHelp();
        return 2;
    }

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

    private static MqttProtocolVersion ParseProtocol(string? value) => value switch
    {
        null or "" or "5" or "5.0" or "v5" => MqttProtocolVersion.V500,
        "3" or "3.1.1" or "v3" or "v311" => MqttProtocolVersion.V311,
        _ => throw new ArgumentException($"Unsupported protocol '{value}'. Use 5 or 3.1.1."),
    };

    private static MqttQoS ParseQos(string? value) => value switch
    {
        null or "" or "0" => MqttQoS.AtMostOnce,
        "1" => MqttQoS.AtLeastOnce,
        "2" => MqttQoS.ExactlyOnce,
        _ => throw new ArgumentException($"Invalid QoS '{value}'."),
    };

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
