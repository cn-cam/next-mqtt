using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NextMqtt.Server.Networking;

namespace NextMqtt.Server.Tests;

public sealed class CertificateLoaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "nextmqtt-cert-" + Guid.NewGuid().ToString("N"));

    public CertificateLoaderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A key container may still hold the file on Windows; the temp
            // directory is left behind rather than failing the test run.
        }
    }

    private static X509Certificate2 CreateCertificate(string[] dnsNames)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + dnsNames[0], key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();

        foreach (var name in dnsNames)
        {
            san.AddDnsName(name);
        }

        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));

        // Keep the exportable key: the loader tests write the key back out as PEM.
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    [Theory]
    [InlineData("next-mqtt.deploy-ssl.co", true)]
    [InlineData("NEXT-MQTT.deploy-ssl.co", true)]
    [InlineData("deploy-ssl.co", false)]
    [InlineData("deep.next-mqtt.deploy-ssl.co", false)]
    [InlineData("other.example", false)]
    public void Wildcard_certificate_covers_exactly_one_label(string candidate, bool expected)
    {
        using var certificate = CreateCertificate(["*.deploy-ssl.co"]);

        Assert.Equal(expected, MqttCertificateLoader.CoversDomain(certificate, candidate));
    }

    [Fact]
    public void Exact_san_entries_are_matched()
    {
        using var certificate = CreateCertificate(["localhost", "mqtt.example.test"]);

        Assert.True(MqttCertificateLoader.CoversDomain(certificate, "mqtt.example.test"));
        Assert.True(MqttCertificateLoader.CoversDomain(certificate, "localhost"));
        Assert.False(MqttCertificateLoader.CoversDomain(certificate, "127.0.0.1"));
    }

    [Fact]
    public void Pem_certificate_and_key_are_loaded_together()
    {
        using var source = CreateCertificate(["mqtt.example.test"]);

        var certificatePath = Path.Combine(_directory, "server.pem");
        var keyPath = Path.Combine(_directory, "server.key");

        File.WriteAllText(certificatePath, source.ExportCertificatePem());
        File.WriteAllText(keyPath, source.GetRSAPrivateKey()!.ExportPkcs8PrivateKeyPem());

        var bundle = MqttCertificateLoader.Load(certificatePath, keyPath, password: null);

        Assert.True(bundle.Certificate.HasPrivateKey);
        Assert.Equal(source.Thumbprint, bundle.Certificate.Thumbprint);
        Assert.Null(bundle.Chain);
    }

    [Fact]
    public void Sibling_key_file_is_used_when_no_path_is_given()
    {
        using var source = CreateCertificate(["mqtt.example.test"]);

        var certificatePath = Path.Combine(_directory, "wildcard.pem");
        File.WriteAllText(certificatePath, source.ExportCertificatePem());
        File.WriteAllText(
            Path.ChangeExtension(certificatePath, ".key"),
            source.GetRSAPrivateKey()!.ExportPkcs8PrivateKeyPem());

        var bundle = MqttCertificateLoader.Load(certificatePath, keyPath: null, password: null);

        Assert.True(bundle.Certificate.HasPrivateKey);
    }

    [Fact]
    public void Missing_certificate_fails_loudly()
    {
        Assert.Throws<FileNotFoundException>(
            () => MqttCertificateLoader.Load(Path.Combine(_directory, "absent.pem"), null, null));
    }
}
