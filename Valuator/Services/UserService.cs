using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Valuator.Services;

public class User
{
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}

public interface IUserService
{
    Task<bool> RegisterAsync(string login, string password);
    Task<User?> AuthenticateAsync(string login, string password);
    Task<User?> GetUserByLoginAsync(string login);
}

public class UserService : IUserService
{
    private readonly IDatabase _redis;
    private const string UserKeyPrefix = "user:";

    public UserService(IConnectionMultiplexer redis)
    {
        _redis = redis.GetDatabase();
    }

    public async Task<bool> RegisterAsync(string login, string password)
    {
        var existing = await _redis.StringGetAsync($"{UserKeyPrefix}{login}");
        if (!existing.IsNullOrEmpty) return false;

        var user = new User
        {
            Login = login,
            PasswordHash = HashPassword(password),
            UserId = Guid.NewGuid().ToString()
        };
        var json = JsonSerializer.Serialize(user);
        await _redis.StringSetAsync($"{UserKeyPrefix}{login}", json);
        return true;
    }

    public async Task<User?> AuthenticateAsync(string login, string password)
    {
        var json = await _redis.StringGetAsync($"{UserKeyPrefix}{login}");
        if (json.IsNullOrEmpty) return null;

        var user = JsonSerializer.Deserialize<User>(json!);
        if (user != null && VerifyPassword(password, user.PasswordHash))
            return user;
        return null;
    }

    public async Task<User?> GetUserByLoginAsync(string login)
    {
        var json = await _redis.StringGetAsync($"{UserKeyPrefix}{login}");
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<User>(json!);
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
    }
}