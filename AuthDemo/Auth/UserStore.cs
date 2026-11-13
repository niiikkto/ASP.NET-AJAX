using Microsoft.AspNetCore.Identity;

namespace AuthDemo.Auth;

public record User(string Username, string PasswordHash, string[] Roles);

public class UserStore
{
    private readonly PasswordHasher<User> _hasher = new();
    private readonly Dictionary<string, User> _users = new(StringComparer.OrdinalIgnoreCase);

    public UserStore()
    {
        Add("alice", "P@ssw0rd!", "user", "admin");
        Add("bob",   "P@ssw0rd!", "user");
    }

    private void Add(string name, string password, params string[] roles)
    {
        var tmp = new User(name, "", roles);
        _users[name] = tmp with { PasswordHash = _hasher.HashPassword(tmp, password) };
    }

    public User? Find(string username) =>
        _users.TryGetValue(username, out var u) ? u : null;

    public User? Validate(string username, string password)
    {
        var user = Find(username);
        if (user is null) return null;
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : user;
    }
}