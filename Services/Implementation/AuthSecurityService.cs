using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JSAPNEW.Models;
using JSAPNEW.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace JSAPNEW.Services.Implementation
{
    public class AuthSecurityService : IAuthSecurityService
    {
        private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);
        private readonly IDataProtector _protector;

        public AuthSecurityService(IDataProtectionProvider dataProtectionProvider)
        {
            _protector = dataProtectionProvider.CreateProtector("JSAP.RefreshToken.v1");
        }

        public Task<bool> IsAccessTokenRevokedAsync(string jti)
            => Task.FromResult(false);

        public Task RevokeAccessTokenAsync(string jti)
            => Task.CompletedTask;

        public Task<string> GenerateRefreshTokenAsync(int userId, string ipAddress)
            => GenerateRefreshTokenAsync(userId, ipAddress, string.Empty, "User");

        public Task<string> GenerateRefreshTokenAsync(int userId, string ipAddress, string userAgent, string role)
        {
            var record = new StatelessRefreshToken
            {
                UserId = userId,
                Role = string.IsNullOrWhiteSpace(role) ? "User" : role,
                ExpiresUtc = DateTime.UtcNow.Add(RefreshTokenLifetime),
                CreatedUtc = DateTime.UtcNow,
                Nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                IpAddress = ipAddress ?? string.Empty,
                UserAgentHash = HashValue(userAgent ?? string.Empty)
            };

            return Task.FromResult(_protector.Protect(JsonSerializer.Serialize(record)));
        }

        public Task<bool> ValidateRefreshTokenAsync(string token, int userId)
            => ValidateRefreshTokenAsync(token, userId, string.Empty, string.Empty);

        public async Task<bool> ValidateRefreshTokenAsync(string token, int userId, string ipAddress, string userAgent)
        {
            var result = await ValidateRefreshTokenAsync(token, ipAddress, userAgent);
            return result.IsValid && result.UserId == userId;
        }

        public Task<(bool IsValid, int UserId, string Role)> ValidateRefreshTokenAsync(string token, string ipAddress, string userAgent)
        {
            var record = Unprotect(token);
            if (record == null || record.ExpiresUtc <= DateTime.UtcNow || record.UserId <= 0)
                return Task.FromResult((false, 0, string.Empty));

            if (!string.IsNullOrWhiteSpace(record.UserAgentHash) &&
                record.UserAgentHash != HashValue(userAgent ?? string.Empty))
            {
                return Task.FromResult((false, 0, string.Empty));
            }

            if (!string.IsNullOrWhiteSpace(record.IpAddress) &&
                !string.IsNullOrWhiteSpace(ipAddress) &&
                !string.Equals(record.IpAddress, ipAddress, StringComparison.Ordinal))
            {
                return Task.FromResult((false, 0, string.Empty));
            }

            return Task.FromResult((true, record.UserId, string.IsNullOrWhiteSpace(record.Role) ? "User" : record.Role));
        }

        public Task RevokeRefreshTokenAsync(string token, string ipAddress, string? replacedByToken = null)
            => Task.CompletedTask;

        public Task RevokeAllUserTokensAsync(int userId)
            => Task.CompletedTask;

        private StatelessRefreshToken? Unprotect(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            try
            {
                return JsonSerializer.Deserialize<StatelessRefreshToken>(_protector.Unprotect(token));
            }
            catch
            {
                return null;
            }
        }

        private static string HashValue(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToBase64String(hash);
        }

        private sealed class StatelessRefreshToken
        {
            public int UserId { get; set; }
            public string Role { get; set; } = "User";
            public DateTime ExpiresUtc { get; set; }
            public DateTime CreatedUtc { get; set; }
            public string Nonce { get; set; } = string.Empty;
            public string IpAddress { get; set; } = string.Empty;
            public string UserAgentHash { get; set; } = string.Empty;
        }
    }
}
