using System.Security.Claims;
using System.Text;
using AuthDemo.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// ---------- 1. Настройки и сервисы ----------
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("В конфигурации нет секции Jwt");
if (Encoding.UTF8.GetByteCount(jwt.Key) < 32)
    throw new InvalidOperationException("Jwt:Key не задан или короче 32 байт (см. user-secrets)");

builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<RefreshStore>();
builder.Services.AddSingleton<RevocationList>();
builder.Services.AddSingleton<TokenService>();

// ---------- 2. Аутентификация ----------
builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = "Smart";
        o.DefaultChallengeScheme = "Smart";
    })
    // "Smart" сам ничего не проверяет, только выбирает схему:
    // есть заголовок Authorization: Bearer ... -> JWT, иначе -> cookie
    .AddPolicyScheme("Smart", "Smart", o =>
    {
        o.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.Authorization.ToString()
                .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
    {
        o.Cookie.Name = ".AuthDemo.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/forbidden";

        // для /api возвращаем 401/403, а не редирект на страницу входа
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = 401;
            else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = 403;
            else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };

        // отзыв сессии: если sid в чёрном списке, cookie больше не действует
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var sid = ctx.Principal?.FindFirstValue("sid");
            var revoked = ctx.HttpContext.RequestServices.GetRequiredService<RevocationList>();
            if (sid is null || revoked.IsRevoked(sid))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
    {
        o.MapInboundClaims = false; // оставляем имена claim как в токене: sub, role, jti
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,           ValidIssuer = jwt.Issuer,
            ValidateAudience = true,         ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                var jti = ctx.Principal?.FindFirstValue("jti");
                var revoked = ctx.HttpContext.RequestServices.GetRequiredService<RevocationList>();
                if (jti is not null && revoked.IsRevoked(jti)) ctx.Fail("Token revoked");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();

// Ключи шифрования cookie. В продакшене храните их вне контейнера/сервера:
builder.Services.AddDataProtection().SetApplicationName("AuthDemo");
//    .PersistKeysToFileSystem(new DirectoryInfo("/var/keys"));

// ---------- 3. Конвейер (порядок важен!) ----------
var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ---------- 4. Страницы (cookie) ----------
app.MapGet("/login", (HttpContext ctx, IAntiforgery af) =>
{
    var t = af.GetAndStoreTokens(ctx);
    var error = ctx.Request.Query.ContainsKey("error")
        ? "<p style=\"color:red\">Неверный логин или пароль</p>" : "";
    var html = $"""
        <!doctype html><meta charset="utf-8"><title>Вход</title>
        <h1>Вход</h1>{error}
        <form method="post" action="/login">
          <input type="hidden" name="{t.FormFieldName}" value="{t.RequestToken}">
          <p><input name="username" placeholder="Логин" required></p>
          <p><input name="password" type="password" placeholder="Пароль" required></p>
          <p><label><input type="checkbox" name="rememberMe" value="true"> Запомнить меня</label></p>
          <button>Войти</button>
        </form>
        """;
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/login", async (HttpContext ctx, [FromForm] LoginForm form, UserStore users) =>
{
    // токен antiforgery проверяется автоматически, т.к. данные берутся из формы
    var user = users.Validate(form.Username, form.Password);
    if (user is null) return Results.Redirect("/login?error=1");

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.NameIdentifier, user.Username),
        new("sid", Guid.NewGuid().ToString("N")) // id сессии, нужен для отзыва
    };
    claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

    var principal = new ClaimsPrincipal(
        new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

    var remember = form.RememberMe == "true";
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
        new AuthenticationProperties
        {
            IsPersistent = remember,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(remember ? 24 * 7 : 8),
            AllowRefresh = true
        });

    return Results.Redirect("/profile");
});

app.MapGet("/profile", (HttpContext ctx, IAntiforgery af) =>
{
    var t = af.GetAndStoreTokens(ctx);
    var name = System.Net.WebUtility.HtmlEncode(ctx.User.Identity?.Name);
    var roles = System.Net.WebUtility.HtmlEncode(string.Join(", ",
        ctx.User.Claims.Where(c => c.Type is ClaimTypes.Role or "role").Select(c => c.Value)));
    var html = $"""
        <!doctype html><meta charset="utf-8"><title>Профиль</title>
        <h1>Привет, {name}!</h1><p>Роли: {roles}</p>
        <form method="post" action="/logout">
          <input type="hidden" name="{t.FormFieldName}" value="{t.RequestToken}">
          <button>Выйти</button>
        </form>
        """;
    return Results.Content(html, "text/html; charset=utf-8");
}).RequireAuthorization();

app.MapPost("/logout", async (HttpContext ctx, IAntiforgery af, RevocationList revoked) =>
{
    try
    {
        await af.ValidateRequestAsync(ctx);   // проверка CSRF-токена из формы
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest("Invalid antiforgery token");
    }

    var sid = ctx.User.FindFirstValue("sid");
    if (sid is not null) revoked.Revoke(sid);
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization();

app.MapGet("/forbidden", () => Results.StatusCode(StatusCodes.Status403Forbidden));

// ---------- 5. API (JWT, но cookie тоже сработает) ----------
var api = app.MapGroup("/api");

api.MapPost("/token", (TokenRequest req, UserStore users, TokenService tokens) =>
{
    var user = users.Validate(req.Username, req.Password);
    return user is null ? Results.Unauthorized() : Results.Ok(tokens.Issue(user));
});

api.MapPost("/token/refresh", (RefreshRequest req, UserStore users,
                               RefreshStore refresh, TokenService tokens) =>
{
    var entry = refresh.Take(req.RefreshToken);          // одноразовый: старый уже удалён
    if (entry is null || entry.Expires < DateTimeOffset.UtcNow) return Results.Unauthorized();

    var user = users.Find(entry.Username);
    return user is null ? Results.Unauthorized() : Results.Ok(tokens.Issue(user));
});

api.MapPost("/logout", (HttpContext ctx, RefreshRequest req,
                        RefreshStore refresh, RevocationList revoked) =>
{
    refresh.Take(req.RefreshToken);
    var jti = ctx.User.FindFirstValue("jti");
    if (jti is not null) revoked.Revoke(jti);
    return Results.NoContent();
}).RequireAuthorization();

api.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
{
    name = user.Identity?.Name,
    roles = user.Claims.Where(c => c.Type is ClaimTypes.Role or "role").Select(c => c.Value)
})).RequireAuthorization();

api.MapGet("/admin", () => Results.Ok("admin area"))
   .RequireAuthorization(p => p.RequireRole("admin"));

app.Run();