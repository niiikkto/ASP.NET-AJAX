using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace AuthDemo.Auth;

public record TokenResponse(
    [property: JsonPropertyName("access_token")]  string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("token_type")]    string TokenType,
    [property: JsonPropertyName("expires_in")]    int ExpiresIn);

public class TokenService(JwtOptions opt, RefreshStore refreshStore)
{
    public TokenResponse Issue(User user)
    {
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange(user.Roles.Select(r => new Claim("role", r)));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.Key)),
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            opt.Issuer, opt.Audience, claims,
            notBefore: now, expires: now.AddMinutes(opt.AccessMinutes),
            signingCredentials: creds);

        var access = new JwtSecurityTokenHandler().WriteToken(jwt);

        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        refreshStore.Save(user.Username, refresh, DateTimeOffset.UtcNow.AddDays(opt.RefreshDays));

        return new TokenResponse(access, refresh, "Bearer", opt.AccessMinutes * 60);
    }
}