namespace JSAPNEW.Services.Interfaces
{
    public interface ITokenRevocationService
    {
        void Revoke(string jti, DateTime expiresUtc);
        bool IsRevoked(string jti);
    }
}
