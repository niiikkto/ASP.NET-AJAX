using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace AuthDemo.Auth;

public class RefreshStore
{
    public record Entry(string Username, DateTimeOffset Expires);
    private readonly ConcurrentDictionary<string, Entry> _map = new();

    // храним хэш токена, а не сам токен
    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public void Save(string username, string token, DateTimeOffset expires) =>
        _map[Hash(token)] = new Entry(username, expires);

    // достаёт и сразу удаляет: refresh-токен одноразовый (rotation)
    public Entry? Take(string token) =>
        _map.TryRemove(Hash(token), out var e) ? e : null;
}

public class RevocationList
{
    private readonly ConcurrentDictionary<string, byte> _set = new();
    public void Revoke(string id) => _set[id] = 0;
    public bool IsRevoked(string id) => _set.ContainsKey(id);
}