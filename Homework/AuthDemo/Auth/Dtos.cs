namespace AuthDemo.Auth;

public record LoginForm(string Username, string Password, string? RememberMe);
public record TokenRequest(string Username, string Password);
public record RefreshRequest(string RefreshToken);

public class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 7;
}