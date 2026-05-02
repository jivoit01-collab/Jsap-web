namespace JSAPNEW.Services.Interfaces
{
    public interface IAuthSecurityService
    {
        Task<bool> IsAccessTokenRevokedAsync(string jti);
        Task RevokeAccessTokenAsync(string jti);
        Task<string> GenerateRefreshTokenAsync(int userId, string ipAddress);
        Task<string> GenerateRefreshTokenAsync(int userId, string ipAddress, string userAgent, string role);
        Task<bool> ValidateRefreshTokenAsync(string token, int userId);
        Task<bool> ValidateRefreshTokenAsync(string token, int userId, string ipAddress, string userAgent);
        Task<(bool IsValid, int UserId, string Role)> ValidateRefreshTokenAsync(string token, string ipAddress, string userAgent);
        Task RevokeRefreshTokenAsync(string token, string ipAddress, string? replacedByToken = null);
        Task RevokeAllUserTokensAsync(int userId);
    }
}
