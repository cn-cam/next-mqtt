using System.Text;
using NextMqtt.Broker.Diagnostics;

namespace NextMqtt.Server.Api;

/// <summary>Renders Prometheus text exposition format (version 0.0.4).</summary>
public static class PrometheusFormatter
{
    public static string Format(BrokerStats stats, int clusterNodes, string version)
    {
        var builder = new StringBuilder(2048);

        Counter(builder, "nextmqtt_connections_opened_total", "MQTT connections accepted", stats.ConnectionsOpened);
        Counter(builder, "nextmqtt_connections_closed_total", "MQTT connections closed", stats.ConnectionsClosed);
        Counter(builder, "nextmqtt_connections_rejected_total", "MQTT connections rejected during CONNECT", stats.ConnectionsRejected);
        Counter(builder, "nextmqtt_authentication_failures_total", "Failed authentication attempts", stats.AuthFailures);
        Counter(builder, "nextmqtt_publishes_received_total", "PUBLISH packets received", stats.PublishesReceived);
        Counter(builder, "nextmqtt_publishes_sent_total", "PUBLISH packets delivered", stats.PublishesSent);
        Counter(builder, "nextmqtt_bytes_received_total", "MQTT bytes received", stats.BytesReceived);
        Counter(builder, "nextmqtt_bytes_sent_total", "MQTT bytes sent", stats.BytesSent);
        Counter(builder, "nextmqtt_messages_dropped_total", "Messages dropped by policy or back-pressure", stats.DroppedMessages);
        Counter(builder, "nextmqtt_retransmissions_total", "QoS retransmissions", stats.Retransmissions);
        Counter(builder, "nextmqtt_subscriptions_created_total", "Successful subscriptions", stats.SubscriptionsCreated);
        Counter(builder, "nextmqtt_sessions_expired_total", "Sessions removed after expiry", stats.SessionsExpired);
        Counter(builder, "nextmqtt_connections_rate_limited_total", "CONNECTs rejected by the connection rate limit", stats.ConnectionsRateLimited);
        Counter(builder, "nextmqtt_publishes_rate_limited_total", "PUBLISH packets dropped by the per-client rate limit", stats.PublishesRateLimited);
        Gauge(builder, "nextmqtt_clients_connected", "Clients currently connected", stats.ConnectedClients);
        Gauge(builder, "nextmqtt_sessions", "Sessions in the registry", stats.Sessions);
        Gauge(builder, "nextmqtt_subscriptions", "Active subscriptions", stats.Subscriptions);
        Gauge(builder, "nextmqtt_retained_messages", "Stored retained messages", stats.RetainedMessages);
        Gauge(builder, "nextmqtt_cluster_nodes", "Cluster members", clusterNodes);
        Gauge(builder, "nextmqtt_build_info", "Build information", 1, ("version", version));

        return builder.ToString();
    }

    private static void Counter(StringBuilder builder, string name, string help, long value)
    {
        builder.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
        builder.Append("# TYPE ").Append(name).Append(" counter\n");
        builder.Append(name).Append(' ').Append(value).Append('\n');
    }

    private static void Gauge(StringBuilder builder, string name, string help, long value, params (string Key, string Value)[] labels)
    {
        builder.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
        builder.Append("# TYPE ").Append(name).Append(" gauge\n");
        builder.Append(name);

        if (labels.Length > 0)
        {
            builder.Append('{');
            for (var i = 0; i < labels.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(labels[i].Key).Append("=\"").Append(Escape(labels[i].Value)).Append('"');
            }

            builder.Append('}');
        }

        builder.Append(' ').Append(value).Append('\n');
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
