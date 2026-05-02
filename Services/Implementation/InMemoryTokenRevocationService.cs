using System.Collections.Concurrent;
using JSAPNEW.Services.Interfaces;

namespace JSAPNEW.Services.Implementation
{
    public class InMemoryTokenRevocationService : ITokenRevocationService
    {
        private readonly ConcurrentDictionary<string, DateTime> _revokedTokens = new(StringComparer.Ordinal);

        public void Revoke(string jti, DateTime expiresUtc)
        {
            if (string.IsNullOrWhiteSpace(jti))
                return;

            _revokedTokens[jti] = expiresUtc;
            RemoveExpired();
        }

        public bool IsRevoked(string jti)
        {
            if (string.IsNullOrWhiteSpace(jti))
                return false;

            RemoveExpired();
            return _revokedTokens.ContainsKey(jti);
        }

        private void RemoveExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var token in _revokedTokens.Where(t => t.Value <= now).ToList())
            {
                _revokedTokens.TryRemove(token.Key, out _);
            }
        }
    }
}
