// =============================================================================
// NasSystem.Infrastructure - JwtService.cs
// JWT 服务实现
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NasSystem.Application.Common;
using NasSystem.Domain.Entities;

/// <summary>
/// JWT 服务实现
/// </summary>
public sealed class JwtService : IJwtService
{
    private readonly JwtSettings _settings;
    private readonly SigningCredentials _signingCredentials;

    // 时间源注入（CONVENTIONS §5.1 / 审计 F-028）：令牌签发/过期时间可测试
    private readonly NasSystem.Domain.Services.IDateTimeProvider _dateTime;

    public JwtService(IOptions<JwtSettings> settings, NasSystem.Domain.Services.IDateTimeProvider dateTime)
    {
        _settings = settings.Value;
        _dateTime = dateTime;

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
    }

    public string GenerateAccessToken(User user, TimeSpan? lifetime = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(_dateTime.UtcNow).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
        };

        // 添加可选声明
        if (!string.IsNullOrEmpty(user.Email))
            claims.Add(new Claim(ClaimTypes.Email, user.Email));

        if (!string.IsNullOrEmpty(user.DisplayName))
            claims.Add(new Claim("display_name", user.DisplayName));

        var expires = _dateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(_settings.AccessTokenExpirationMinutes));

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: _dateTime.UtcNow,
            expires: expires,
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public DateTime? GetTokenExpiration(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(token);
            return jwtToken.ValidTo;
        }
        catch
        {
            return null;
        }
    }
}
