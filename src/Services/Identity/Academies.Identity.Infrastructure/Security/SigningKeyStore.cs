using System.Security.Cryptography;
using Academies.BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Academies.Identity.Infrastructure.Security;

/// <summary>
/// The RSA key that signs access tokens. It is loaded from <c>Jwt:SigningKeyPem</c> (PEM text,
/// e.g. from a secret) or from the file at <c>Jwt:SigningKeyPath</c>. In Development a missing file
/// is generated on first run. In any other environment the key must be provided, because each
/// replica generating its own key would reject the others' tokens.
/// </summary>
public sealed class SigningKeyStore
{
    private SigningKeyStore(RSA rsa)
    {
        var publicParameters = rsa.ExportParameters(includePrivateParameters: false);
        KeyId = Base64UrlEncoder.Encode(SHA256.HashData(publicParameters.Modulus!)[..8]);
        SigningKey = new RsaSecurityKey(rsa) { KeyId = KeyId };
        Modulus = Base64UrlEncoder.Encode(publicParameters.Modulus);
        Exponent = Base64UrlEncoder.Encode(publicParameters.Exponent);
    }

    public string KeyId { get; }
    public RsaSecurityKey SigningKey { get; }
    public SigningCredentials Credentials => new(SigningKey, SecurityAlgorithms.RsaSha256);

    /// <summary>Base64url RSA modulus (JWK <c>n</c>).</summary>
    public string Modulus { get; }

    /// <summary>Base64url RSA exponent (JWK <c>e</c>).</summary>
    public string Exponent { get; }

    public static SigningKeyStore Load(IConfiguration configuration, string contentRoot, bool allowGenerate)
    {
        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        var rsa = RSA.Create();

        var pem = configuration["Jwt:SigningKeyPem"];
        if (!string.IsNullOrWhiteSpace(pem))
        {
            rsa.ImportFromPem(pem);
            return new SigningKeyStore(rsa);
        }

        var path = Path.IsPathRooted(jwt.SigningKeyPath) ? jwt.SigningKeyPath : Path.Combine(contentRoot, jwt.SigningKeyPath);
        if (File.Exists(path))
        {
            rsa.ImportFromPem(File.ReadAllText(path));
            return new SigningKeyStore(rsa);
        }

        if (!allowGenerate)
        {
            throw new InvalidOperationException(
                $"No JWT signing key: set Jwt:SigningKeyPem or place a PEM at '{path}'. " +
                "Generate one with: openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out signing.pem");
        }

        rsa.KeySize = 2048;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());
        return new SigningKeyStore(rsa);
    }
}
