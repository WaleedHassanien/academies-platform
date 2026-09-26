using System.Security.Cryptography;
using System.Text;
using Academies.Academic.Application;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Academies.Academic.Infrastructure;

/// <summary>
/// <c>Meetings</c> configuration. <see cref="Provider"/>:
/// <list type="bullet">
/// <item><c>Public</c>: rooms on <see cref="BaseUrl"/>. meet.jit.si makes the first person log in; community
/// servers such as meet.ffmuc.net don't, but whoever arrives first becomes moderator and there's no service guarantee.</item>
/// <item><c>JaaS</c>: 8x8 Jitsi as a Service. Each person gets a token signed with the JaaS key, so the
/// teacher opens their room as moderator with their name and email from the system, without logging in.</item>
/// <item><c>SelfHosted</c>: your own Jitsi server with token authentication (a shared app secret).</item>
/// </list>
/// </summary>
public sealed class MeetingOptions
{
    public const string Section = "Meetings";

    public string Provider { get; set; } = "Public";
    public string BaseUrl { get; set; } = "https://meet.jit.si";

    /// <summary>Makes teachers' room names unguessable; changing it gives every teacher a new room.</summary>
    public string RoomSecret { get; set; } = "dev-room-secret-change-me";

    public JaasOptions JaaS { get; set; } = new();
    public SelfHostedJitsiOptions SelfHosted { get; set; } = new();

    public sealed class JaasOptions
    {
        /// <summary>The App ID from the JaaS console, e.g. "vpaas-magic-cookie-1a2b3c...".</summary>
        public string AppId { get; set; } = string.Empty;

        /// <summary>The API key's full id from the JaaS console, e.g. "vpaas-magic-cookie-1a2b3c.../4f5e6d".</summary>
        public string KeyId { get; set; } = string.Empty;

        /// <summary>The API key's private key (PEM text), or <see cref="PrivateKeyPath"/> to the downloaded .pk file.</summary>
        public string? PrivateKeyPem { get; set; }

        public string? PrivateKeyPath { get; set; }
    }

    public sealed class SelfHostedJitsiOptions
    {
        /// <summary>Your Jitsi server's host name, e.g. "meet.myacademy.com".</summary>
        public string Domain { get; set; } = string.Empty;

        /// <summary>prosody's app_id and app_secret (token authentication).</summary>
        public string AppId { get; set; } = string.Empty;

        public string AppSecret { get; set; } = string.Empty;
    }
}

/// <summary>
/// Teachers' Jitsi rooms (US-037). Each teacher's room name is fixed and unguessable (an HMAC of the
/// academy and teacher). With a token provider, <see cref="JoinUrl"/> signs a short-lived per-person JWT
/// for that one room; without one it only pre-fills the person's name and email.
/// </summary>
internal sealed class JitsiMeetingLinkGenerator : IMeetingLinkGenerator
{
    private readonly MeetingOptions _options;
    private readonly SigningCredentials? _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JitsiMeetingLinkGenerator(MeetingOptions options)
    {
        _options = options;
        _credentials = Provider switch
        {
            "jaas" => JaasCredentials(options.JaaS),
            "selfhosted" => SelfHostedCredentials(options.SelfHosted),
            _ => null,
        };
    }

    private string Provider => _options.Provider.Trim().ToLowerInvariant();

    /// <summary>Where rooms live: 8x8.vc/{app id}, your own domain, or the public base URL.</summary>
    private string RoomBase => Provider switch
    {
        "jaas" => $"https://8x8.vc/{_options.JaaS.AppId}",
        "selfhosted" => $"https://{_options.SelfHosted.Domain.Trim().TrimEnd('/')}",
        _ => _options.BaseUrl.TrimEnd('/'),
    };

    public string RoomUrl(long academyId, long teacherUserId) => $"{RoomBase}/{RoomName(academyId, teacherUserId)}";

    /// <summary>Our rooms (current or an earlier provider) are the ones named "academy…"; anything else was typed in by hand.</summary>
    public bool IsOurRoom(string meetingUrl) =>
        Uri.TryCreate(meetingUrl, UriKind.Absolute, out var uri) && uri.Segments.Last().StartsWith("academy", StringComparison.Ordinal);

    public string JoinUrl(long academyId, long teacherUserId, MeetingParticipant participant, DateTime notBeforeUtc, DateTime expiresAtUtc)
    {
        var room = RoomName(academyId, teacherUserId);
        var url = $"{RoomBase}/{room}";
        if (_credentials is not null)
        {
            url += $"?jwt={Token(room, participant, notBeforeUtc, expiresAtUtc)}";
        }

        // Skip Jitsi's "enter your name" screen and show the person's own name.
        return url + "#config.prejoinConfig.enabled=false"
                   + $"&userInfo.displayName={Uri.EscapeDataString(Quote(participant.Name))}"
                   + $"&userInfo.email={Uri.EscapeDataString(Quote(participant.Email))}";
    }

    /// <summary>"academy3-t10-9f2c4e1a7b3d5e60": stable per teacher, impossible to guess for anyone else.</summary>
    private string RoomName(long academyId, long teacherUserId)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.RoomSecret), Encoding.UTF8.GetBytes($"{academyId}:{teacherUserId}"));
        return $"academy{academyId}-t{teacherUserId}-{Convert.ToHexStringLower(mac)[..16]}";
    }

    private string Token(string room, MeetingParticipant p, DateTime notBeforeUtc, DateTime expiresAtUtc)
    {
        var user = new Dictionary<string, object>
        {
            ["id"] = p.UserId.ToString(),
            ["name"] = p.Name,
            ["email"] = p.Email,
            ["moderator"] = p.IsModerator ? "true" : "false",
        };

        var claims = Provider == "jaas"
            ? new Dictionary<string, object>
            {
                ["aud"] = "jitsi",
                ["iss"] = "chat",
                ["sub"] = _options.JaaS.AppId,
                ["room"] = room,
                ["context"] = new Dictionary<string, object>
                {
                    ["user"] = user,
                    ["features"] = new Dictionary<string, object>
                    {
                        ["livestreaming"] = "false", ["recording"] = "false", ["transcription"] = "false", ["outbound-call"] = "false",
                    },
                },
            }
            : new Dictionary<string, object>
            {
                ["aud"] = _options.SelfHosted.AppId,
                ["iss"] = _options.SelfHosted.AppId,
                ["sub"] = _options.SelfHosted.Domain,
                ["room"] = room,
                ["moderator"] = p.IsModerator,
                ["context"] = new Dictionary<string, object> { ["user"] = user },
            };

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            NotBefore = notBeforeUtc,
            IssuedAt = DateTime.UtcNow,
            Expires = expiresAtUtc,
            SigningCredentials = _credentials,
        });
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "'")}\"";

    private static SigningCredentials JaasCredentials(MeetingOptions.JaasOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.AppId) || string.IsNullOrWhiteSpace(o.KeyId))
        {
            throw new InvalidOperationException("Meetings:JaaS:AppId and Meetings:JaaS:KeyId are required for the JaaS provider.");
        }

        var pem = !string.IsNullOrWhiteSpace(o.PrivateKeyPem) ? o.PrivateKeyPem
            : !string.IsNullOrWhiteSpace(o.PrivateKeyPath) ? File.ReadAllText(o.PrivateKeyPath)
            : throw new InvalidOperationException("Set Meetings:JaaS:PrivateKeyPem or Meetings:JaaS:PrivateKeyPath.");

        var rsa = RSA.Create();
        rsa.ImportFromPem(pem.Replace("\\n", "\n"));
        return new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = o.KeyId }, SecurityAlgorithms.RsaSha256);
    }

    private static SigningCredentials SelfHostedCredentials(MeetingOptions.SelfHostedJitsiOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.Domain) || string.IsNullOrWhiteSpace(o.AppId) || string.IsNullOrWhiteSpace(o.AppSecret))
        {
            throw new InvalidOperationException("Meetings:SelfHosted:Domain, AppId and AppSecret are required for a self-hosted Jitsi.");
        }

        // HS256 signing here needs a key of at least 256 bits, so the shared secret must be long enough.
        var key = Encoding.UTF8.GetBytes(o.AppSecret);
        if (key.Length < 32)
        {
            throw new InvalidOperationException("Meetings:SelfHosted:AppSecret must be at least 32 characters.");
        }

        return new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);
    }
}
