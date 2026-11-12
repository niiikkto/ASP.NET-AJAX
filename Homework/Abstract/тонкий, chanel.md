Chanels в ADO.NET

```c#
namespace WebApplication8
{
    using System;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;

    // ============================================================
    // 1. Определение интерфейса очереди
    // ============================================================
    public interface IBackgroundTaskQueue
    {
        ValueTask QueueAsync(Func<CancellationToken, ValueTask> workItem);
        ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(CancellationToken ct);
    }

    // ============================================================
    // 2. Реализация очереди с Bounded Channel
    // ============================================================
    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly Channel<Func<CancellationToken, ValueTask>> _channel;

        public BackgroundTaskQueue(int capacity = 100)
        {
            var options = new BoundedChannelOptions(capacity)
            {
                // Режим ожидания при заполнении: WriteAsync будет ждать,
                // пока потребитель не освободит место
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,   // только один BackgroundService читает
                SingleWriter = false,  // веб-запросы конкурентно записывают
                AllowSynchronousContinuations = false
            };

            _channel = Channel.CreateBounded<Func<CancellationToken, ValueTask>>(options);
        }

        // Производитель: вызывается из веб-запроса
        public async ValueTask QueueAsync(Func<CancellationToken, ValueTask> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            await _channel.Writer.WriteAsync(workItem);
        }

        // Потребитель: вызывается из BackgroundService
        public async ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(CancellationToken ct)
        {
            return await _channel.Reader.ReadAsync(ct);
        }
    }

    // ============================================================
    // 3. BackgroundService — потребительский цикл
    // ============================================================
    public class QueueConsumerService : BackgroundService
    {
        private readonly IBackgroundTaskQueue _queue;
        private readonly ILogger<QueueConsumerService> _logger;

        public QueueConsumerService(IBackgroundTaskQueue queue, ILogger<QueueConsumerService> logger)
        {
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("QueueConsumerService запущен.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Асинхронное ожидание задачи из очереди
                    var workItem = await _queue.DequeueAsync(stoppingToken);

                    _logger.LogInformation("Начата обработка задачи в потоке {ThreadId}", Environment.CurrentManagedThreadId);
                    await workItem(stoppingToken);
                    _logger.LogInformation("Задача завершена.");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // При остановке приложения выход из цикла
                    break;
                }
                catch (Exception ex)
                {
                    // Логирование исключения, но не завершение службы
                    _logger.LogError(ex, "Ошибка при обработке фоновой задачи.");
                }
            }

            _logger.LogInformation("QueueConsumerService остановлен.");
        }
    }

    // ============================================================
    // 4. Пример использования: имитация веб-запроса, ставящего задачу в очередь
    // ============================================================
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    // Регистрация очереди как singleton
                    services.AddSingleton<IBackgroundTaskQueue>(new BackgroundTaskQueue(capacity: 5));
                    // Регистрация BackgroundService
                    services.AddHostedService<QueueConsumerService>();
                })
                .Build();

            await host.StartAsync();

            // Имитация трёх веб-запросов, ставящих задачи в очередь
            var queue = host.Services.GetRequiredService<IBackgroundTaskQueue>();

            for (int i = 1; i <= 3; i++)
            {
                int taskId = i;
                await queue.QueueAsync(async ct =>
                {
                    // Имитация длительной работы
                    await Task.Delay(1000, ct);
                    Console.WriteLine($"  [Задача {taskId}] выполнена в потоке {Environment.CurrentManagedThreadId}");
                });
                Console.WriteLine($"Задача {taskId} поставлена в очередь.");
            }

            // Ожидание завершения обработки
            await Task.Delay(4000);

            // Корректная остановка хоста
            await host.StopAsync();
            host.Dispose();
        }
    }
}

```




Тонкий клиент



```c#
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace ThinClient.Http;

/// <summary>
/// Унифицированный результат запроса.
/// </summary>
public readonly struct ApiResult<T>
{
    public bool IsSuccess { get; }
    public T? Data { get; }
    public ApiError? Error { get; }
    public HttpStatusCode StatusCode { get; }

    private ApiResult(bool ok, T? data, ApiError? error, HttpStatusCode status)
    {
        IsSuccess = ok;
        Data = data;
        Error = error;
        StatusCode = status;
    }

    public static ApiResult<T> Ok(T data, HttpStatusCode status) =>
        new(true, data, null, status);

    public static ApiResult<T> Fail(ApiError error, HttpStatusCode status) =>
        new(false, default, error, status);
}

public enum ApiErrorKind
{
    Network,
    Timeout,
    Http,
    Parse,
    Canceled
}

public sealed record ApiError(
    ApiErrorKind Kind,
    string Message,
    HttpStatusCode? StatusCode = null,
    string? Code = null,
    object? Details = null);


public sealed class HttpTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly Func<string?>? _getToken;

    public HttpTransport(
        HttpClient client,
        string baseUrl,
        Func<string?>? getToken = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _baseUrl = baseUrl.TrimEnd('/');
        _getToken = getToken;
    }

    public Task<ApiResult<T>> GetAsync<T>(
        string path,
        CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Get, path, null, ct);

    public Task<ApiResult<T>> PostAsync<T>(
        string path,
        object? body = null,
        CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Post, path, body, ct);

    public Task<ApiResult<T>> PutAsync<T>(
        string path,
        object? body = null,
        CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Put, path, body, ct);

    public Task<ApiResult<T>> PatchAsync<T>(
        string path,
        object? body = null,
        CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Patch, path, body, ct);

    public Task<ApiResult<T>> DeleteAsync<T>(
        string path,
        CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Delete, path, null, ct);

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct)
    {
        var url = $"{_baseUrl}/{path.TrimStart('/')}";

        using var request = new HttpRequestMessage(method, url);

        // Заголовки
        request.Headers.Accept.ParseAdd("application/json");

        var token = _getToken?.Invoke();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Тело
        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            return ApiResult<T>.Fail(
                new ApiError(ApiErrorKind.Canceled, "Запрос отменён"),
                HttpStatusCode.RequestTimeout);
        }
        catch (TaskCanceledException)
        {
            // HttpClient кидает TaskCanceledException и на таймаут, и на отмену.
            // Если ct не запрошен — это таймаут.
            return ApiResult<T>.Fail(
                new ApiError(ApiErrorKind.Timeout, "Таймаут запроса"),
                HttpStatusCode.RequestTimeout);
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<T>.Fail(
                new ApiError(ApiErrorKind.Network, ex.Message),
                HttpStatusCode.ServiceUnavailable);
        }

        // Парсинг тела
        T? parsed;
        try
        {
            var bodyText = await response.Content.ReadAsStringAsync(ct);
            parsed = string.IsNullOrWhiteSpace(bodyText)
                ? default
                : JsonSerializer.Deserialize<T>(bodyText, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ApiResult<T>.Fail(
                new ApiError(ApiErrorKind.Parse, ex.Message),
                response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            // Пытаемся вытащить структурированную ошибку из тела
            string? code = null;
            string? message = null;
            object? details = null;
            try
            {
                using var doc = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;

                if (root.TryGetProperty("code", out var codeEl))
                    code = codeEl.GetString();
                if (root.TryGetProperty("message", out var msgEl))
                    message = msgEl.GetString();
                if (root.TryGetProperty("details", out var detEl))
                    details = detEl.Clone();
            }
            catch { /* тело не JSON — оставляем null */ }

            return ApiResult<T>.Fail(
                new ApiError(
                    ApiErrorKind.Http,
                    message ?? $"HTTP {(int)response.StatusCode}",
                    response.StatusCode,
                    code,
                    details),
                response.StatusCode);
        }

        return ApiResult<T>.Ok(parsed!, response.StatusCode);
    }
}
public sealed record User(
    string Id,
    string Email,
    string Name);

public sealed record CreateUserRequest(
    string Email,
    string Name);

public sealed record UpdateUserRequest(
    string? Name,
    string? Email);

public sealed record UserListResponse(
    IReadOnlyList<User> Items,
    int Total);

/// <summary>
/// Тонкий клиент конкретного HTTP-контракта.
/// </summary>
public sealed class UsersApi
{
    private readonly HttpTransport _transport;

    public UsersApi(HttpTransport transport)
    {
        _transport = transport;
    }

    public Task<ApiResult<UserListResponse>> ListAsync(
        int page = 1,
        int limit = 20,
        CancellationToken ct = default) =>
        _transport.GetAsync<UserListResponse>(
            $"users?page={page}&limit={limit}", ct);

    public Task<ApiResult<User>> GetByIdAsync(
        string id,
        CancellationToken ct = default) =>
        _transport.GetAsync<User>($"users/{Uri.EscapeDataString(id)}", ct);

    public Task<ApiResult<User>> CreateAsync(
        CreateUserRequest request,
        CancellationToken ct = default) =>
        _transport.PostAsync<User>("users", request, ct);

    public Task<ApiResult<User>> UpdateAsync(
        string id,
        UpdateUserRequest request,
        CancellationToken ct = default) =>
        _transport.PatchAsync<User>($"users/{Uri.EscapeDataString(id)}", request, ct);

    public Task<ApiResult<object?>> DeleteAsync(
        string id,
        CancellationToken ct = default) =>
        _transport.DeleteAsync<object?>($"users/{Uri.EscapeDataString(id)}", ct);
}




public static class Program
{
    // HttpClient должен переиспользоваться!
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30) // общий таймаут
    };

    public static async Task Main()
    {
        var transport = new HttpTransport(
            SharedClient,
            baseUrl: "https://api.example.com/v1",
            getToken: () => Environment.GetEnvironmentVariable("API_TOKEN"));

        var usersApi = new UsersApi(transport);

        // 1. Успешный GET
        var list = await usersApi.ListAsync(page: 1, limit: 10);
        if (list.IsSuccess)
        {
            Console.WriteLine($"Пользователей: {list.Data!.Total}");
            foreach (var u in list.Data.Items)
                Console.WriteLine($"  {u.Id}: {u.Name} <{u.Email}>");
        }
        else
        {
            HandleError(list.Error!);
        }

        // 2. Создание с обработкой валидации
        var created = await usersApi.CreateAsync(
            new CreateUserRequest("ivan@example.com", "Иван"));

        if (created.IsSuccess)
        {
            Console.WriteLine($"Создан: {created.Data!.Id}");
        }
        else if (created.Error!.Kind == ApiErrorKind.Http
              && created.StatusCode == HttpStatusCode.BadRequest)
        {
            Console.WriteLine($"Валидация: {created.Error.Message}");
            Console.WriteLine($"Детали: {created.Error.Details}");
        }
        else
        {
            HandleError(created.Error);
        }

        // 3. Отмена через CancellationToken
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var canceled = await usersApi.GetByIdAsync("42", cts.Token);
        if (!canceled.IsSuccess && canceled.Error!.Kind == ApiErrorKind.Canceled)
        {
            Console.WriteLine("Запрос отменён — это не ошибка UI");
        }

        // 4. 404 — это HTTP-ошибка, а не исключение
        var missing = await usersApi.GetByIdAsync("does-not-exist");
        if (!missing.IsSuccess
         && missing.Error!.Kind == ApiErrorKind.Http
         && missing.StatusCode == HttpStatusCode.NotFound)
        {
            Console.WriteLine("Пользователь не найден");
        }
    }

    private static void HandleError(ApiError error)
    {
        switch (error.Kind)
        {
            case ApiErrorKind.Network:
                Console.Error.WriteLine($"Сеть недоступна: {error.Message}");
                break;
            case ApiErrorKind.Timeout:
                Console.Error.WriteLine("Таймаут запроса");
                break;
            case ApiErrorKind.Parse:
                Console.Error.WriteLine($"Сервер вернул мусор: {error.Message}");
                break;
            case ApiErrorKind.Http:
                Console.Error.WriteLine($"HTTP {(int)error.StatusCode!}: {error.Message}");
                break;
            case ApiErrorKind.Canceled:
                // молча
                break;
        }
    }
}
```