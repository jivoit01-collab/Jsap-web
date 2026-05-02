using JSAPNEW.Services.Interfaces;

namespace JSAPNEW.Services.Implementation
{
    public class DistributedTokenRevocationService : ITokenRevocationService
    {
        public void Revoke(string jti, DateTime expiresUtc)
        {
        }

        public bool IsRevoked(string jti)
            => false;
    }
}
