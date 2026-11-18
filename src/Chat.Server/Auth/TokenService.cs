using System.Security.Claims;
using System.Text;
using Chat.Shared;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Chat.Server.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "chat-server";
    public string Audience { get; set; } = "chat-clients";
    public string SigningKey { get; set; } = "";
    public int LifetimeMinutes { get; set; } = 720;
}

public sealed class TokenService(JwtOptions options)
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _creds = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        SecurityAlgorithms.HmacSha256);

    public TokenResponse Issue(string userName)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(options.LifetimeMinutes);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Expires = expires.UtcDateTime,
            SigningCredentials = _creds,
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userName),
                new Claim(ClaimTypes.Name, userName),
            ]),
        });
        return new TokenResponse(token, expires);
    }
}
