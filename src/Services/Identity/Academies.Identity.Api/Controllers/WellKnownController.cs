using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Academies.Identity.Api.Controllers;

/// <summary>
/// Minimal OpenID discovery. JwtBearer in the other services reads it to find the public key,
/// so they never share the private signing key.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route(".well-known")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class WellKnownController(SigningKeyStore keys, IOptions<JwtOptions> jwt) : ControllerBase
{
    [HttpGet("openid-configuration")]
    public IActionResult Configuration() => Ok(new Dictionary<string, object>
    {
        ["issuer"] = jwt.Value.Issuer,
        ["jwks_uri"] = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/.well-known/jwks.json",
        ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
    });

    [HttpGet("jwks.json")]
    public IActionResult Jwks() => Ok(new
    {
        keys = new[]
        {
            new Dictionary<string, string>
            {
                ["kty"] = "RSA",
                ["use"] = "sig",
                ["alg"] = "RS256",
                ["kid"] = keys.KeyId,
                ["n"] = keys.Modulus,
                ["e"] = keys.Exponent,
            },
        },
    });
}
