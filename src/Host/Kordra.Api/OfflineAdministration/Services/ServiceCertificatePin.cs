using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Kordra.Api;

/// <summary>
/// 12.3d (D2/D6): the pin of a service certificate is the SHA-256 of its SubjectPublicKeyInfo, lowercase hex. Reads public
/// certificates only: any input that carries a private key is refused, so the pin tool never handles key material.
/// </summary>
public static class ServiceCertificatePin
{
    public static string FromCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return Convert.ToHexStringLower(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>Returns the pin of the single PEM certificate, or null for anything else (no certificate, a private key, junk).</summary>
    public static string? FromPem(string pem)
    {
        if (string.IsNullOrEmpty(pem) || pem.Length > 16 * 1024 || pem.Contains("PRIVATE KEY", StringComparison.Ordinal) ||
            pem.Split("-----BEGIN CERTIFICATE-----").Length != 2) return null;
        try
        {
            using var certificate = X509Certificate2.CreateFromPem(pem);
            return certificate.HasPrivateKey ? null : FromCertificate(certificate);
        }
        catch (CryptographicException) { return null; }
    }
}
