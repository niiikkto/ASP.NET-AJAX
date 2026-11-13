# AuthDemo

Учебный проект на ASP.NET Core (Minimal API), показывающий **гибридную аутентификацию**:

- **Cookie** для страниц, открываемых в браузере (форма входа, профиль);
- **JWT Bearer** (access + refresh токены) для API;
- **Policy-схема "Smart"**, которая сама выбирает способ проверки по заголовку `Authorization`.

> Проект учебный. Пользователи и токены хранятся в памяти, перед использованием в продакшене прочитайте раздел [Что доработать для продакшена](#что-доработать-для-продакшена).

## Возможности

- Вход через HTML-форму с защитой от CSRF (antiforgery-токен).
- Выход с отзывом сессии (чёрный список `sid`).
- Получение пары access/refresh токенов по логину и паролю.
- Обновление токенов с ротацией: refresh-токен одноразовый.
- Отзыв access-токена по `jti` при выходе через API.
- Роли и ограничение доступа (`/api/admin` только для роли `admin`).
- Пароли хранятся в виде хэша `PasswordHasher` (PBKDF2 с солью).
- Для `/api/*` вместо редиректа на страницу входа возвращаются коды 401 и 403.

## Требования

- [.NET SDK](https://dotnet.microsoft.com/download) 8.0 или новее
- Доверенный dev-сертификат HTTPS (cookie помечена `Secure`):

```powershell
dotnet dev-certs https --trust
```

## Структура проекта

```
AuthDemo/
├─ Program.cs            # настройка аутентификации, конвейер, все эндпоинты
├─ appsettings.json      # Issuer, Audience, время жизни токенов (без ключа!)
└─ Auth/
   ├─ Dtos.cs            # модели запросов и JwtOptions
   ├─ UserStore.cs       # тестовые пользователи и проверка пароля
   ├─ Stores.cs          # RefreshStore и RevocationList (в памяти)
   └─ TokenService.cs    # выпуск access- и refresh-токенов
```

## Быстрый старт

### 1. Задайте секретный ключ JWT

Ключ не хранится в коде и в `appsettings.json`. В разработке используйте user-secrets. В папке проекта выполните (PowerShell):

```powershell
dotnet user-secrets init
$bytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$key = [Convert]::ToBase64String($bytes)
dotnet user-secrets set "Jwt:Key" $key
```

Для PowerShell 7+ можно короче: `[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))`.

Проверка: `dotnet user-secrets list`. Ключ должен быть не короче 32 байт, иначе приложение не запустится.

В продакшене задайте ключ переменной окружения `Jwt__Key` или через Key Vault.

### 2. Запустите приложение

```powershell
dotnet run --launch-profile https
```

Адреса указаны в консоли и в `Properties/launchSettings.json`. Ниже для примера используется `https://localhost:7093`, у вас порт может отличаться.

## Тестовые пользователи

| Логин | Пароль       | Роли          |
|-------|--------------|---------------|
| alice | `P@ssw0rd!`  | `user, admin` |
| bob   | `P@ssw0rd!`  | `user`        |

## Эндпоинты

### Страницы (cookie)

| Метод | URL         | Доступ        | Описание                                           |
|-------|-------------|---------------|----------------------------------------------------|
| GET   | `/login`    | все           | Форма входа                                        |
| POST  | `/login`    | все           | Вход, выдача cookie, редирект на `/profile`        |
| GET   | `/profile`  | авторизованные| Имя пользователя, роли, кнопка «Выйти»            |
| POST  | `/logout`   | авторизованные| Выход, отзыв сессии                                |
| GET   | `/forbidden`| все           | Ответ 403 при отсутствии прав                      |

### API (JWT или cookie)

| Метод | URL                  | Доступ             | Описание                                    |
|-------|----------------------|--------------------|---------------------------------------------|
| POST  | `/api/token`         | все                | Логин и пароль → access и refresh токены    |
| POST  | `/api/token/refresh` | все                | Обмен refresh-токена на новую пару          |
| POST  | `/api/logout`        | авторизованные     | Отзыв refresh-токена и текущего access      |
| GET   | `/api/me`            | авторизованные     | Имя и роли текущего пользователя            |
| GET   | `/api/admin`         | роль `admin`       | Проверка ролевого доступа                   |

## Проверка работы

### Через браузер (cookie)

1. Откройте `https://localhost:7093/login`.
2. Войдите как `alice` / `P@ssw0rd!`. Откроется `/profile` с ролями `user, admin`.
3. Откройте `/api/admin`. У alice он откроется, у bob вернётся 403.
4. Нажмите «Выйти».

### Через API (JWT)

В PowerShell используйте `curl.exe` (не алиас `curl`) либо Postman.

```bash
# 1. Получить токены
curl -k -X POST https://localhost:7093/api/token \
  -H "Content-Type: application/json" \
  -d '{"username":"alice","password":"P@ssw0rd!"}'

# 2. Вызвать защищённый эндпоинт
curl -k https://localhost:7093/api/me \
  -H "Authorization: Bearer <access_token>"

# 3. Обновить токены (refresh одноразовый)
curl -k -X POST https://localhost:7093/api/token/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refresh_token>"}'
```

Ответ `/api/token`:

```json
{
  "access_token": "eyJ...",
  "refresh_token": "base64...",
  "token_type": "Bearer",
  "expires_in": 900
}
```

## Как это устроено

**Схема "Smart".** Она ничего не проверяет сама, а перенаправляет запрос: если есть заголовок `Authorization: Bearer ...`, то проверяется JWT, иначе cookie. Поэтому `/api/*` доступно и из браузера с cookie, и из клиентов с токеном.

**Cookie.** Имя `.AuthDemo.Auth`, `HttpOnly`, `Secure`, `SameSite=Lax`. Срок жизни 8 часов со скользящим продлением. При включённой галочке «Запомнить меня» cookie становится постоянной на 7 дней. В claims лежит `sid`, по нему сессия отзывается при выходе.

**JWT.** Подпись HMAC-SHA256. Проверяются издатель, аудитория, подпись и срок жизни (допуск 30 секунд). Access-токен живёт 15 минут, refresh-токен 7 дней. В токене есть `sub`, `jti` и роли в claim `role`.

**Refresh-токены.** В хранилище попадает только хэш токена (SHA-256). При обновлении старый токен удаляется и выдаётся новый (ротация).

**CSRF.** Antiforgery-токен вставляется в формы `/login` и `/logout` и проверяется на сервере. JSON-эндпоинты `/api/token*` его не требуют, так как не используют cookie.

**Порядок middleware** в `Program.cs` важен: `UseHttpsRedirection` → `UseAuthentication` → `UseAuthorization` → `UseAntiforgery`.

## Настройки

`appsettings.json`:

```json
{
  "Jwt": {
    "Issuer": "https://authdemo.local",
    "Audience": "authdemo.api",
    "AccessMinutes": 15,
    "RefreshDays": 7
  }
}
```

`Jwt:Key` задаётся отдельно: user-secrets или переменная окружения `Jwt__Key`.

## Частые проблемы

| Проблема | Решение |
|----------|---------|
| Браузер показывает 404 на `/` | Корневой страницы нет, откройте `/login` или добавьте `app.MapGet("/", () => Results.Redirect("/profile"));` |
| Предупреждение о сертификате | `dotnet dev-certs https --trust`, затем перезапустите браузер |
| `Jwt:Key не задан или короче 32 байт` | Выполните шаг 1 из быстрого старта |
| `GetBytes` не найден в PowerShell | Используйте вариант с `New-Object byte[] 48` (Windows PowerShell 5.1) |
| `RequireAntiforgery` не найден | В .NET 8 такого метода нет. В проекте токен проверяется вручную через `IAntiforgery.ValidateRequestAsync` |
| `SetApplicationName` не найден | Добавьте `using Microsoft.AspNetCore.DataProtection;` |
| 400 на `/logout` | Токен antiforgery не передан. Выходите через кнопку на `/profile` |

## Что доработать для продакшена

1. Заменить `UserStore` на базу данных, лучше ASP.NET Core Identity с EF Core. Добавить блокировку после неудачных попыток и ограничение частоты запросов (`AddRateLimiter`) для `/login` и `/api/token`.
2. Перенести `RefreshStore` и `RevocationList` в БД или Redis: сейчас данные теряются при перезапуске и не работают при нескольких экземплярах.
3. Вынести ключи Data Protection в общее хранилище (`PersistKeysToFileSystem`, БД или Redis), иначе cookie перестанут расшифровываться после перезапуска или на втором сервере.
4. Для токенов, которые проверяют несколько сервисов, перейти с HMAC на пару ключей RS256.
5. Включить HSTS и держать приложение только за HTTPS.
6. Хранить секреты в Key Vault или переменных окружения, а не в репозитории.

## Для SPA: вариант BFF

Если фронтенд на React, Vue или Angular и токены не должны попадать в браузер, добавляется отдельный проект BFF. Он выдаёт браузеру cookie, а JWT хранит на сервере по `sid` и подставляет заголовок `Authorization: Bearer` при проксировании запросов к API. В этот проект BFF не входит.