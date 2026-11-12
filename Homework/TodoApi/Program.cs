using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Todo Management API",
        Version = "v1",
        Description = "API для управления задачами. Поддерживает фильтрацию и создание.",
        Contact = new OpenApiContact
        {
            Name = "API Support",
            Email = "support@example.com"
        },
        License = new OpenApiLicense
        {
            Name = "MIT",
            Url = new Uri("https://opensource.org/licenses/MIT")
        }
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Todo Management API v1");
});

var todos = new List<TodoItem>
{
    new(1, "Изучить OpenAPI", true),
    new(2, "Сделать скриншот из консоли", false)
};

app.MapGet("/api/todos", (bool? isCompleted) =>
{
    var result = isCompleted.HasValue 
        ? todos.Where(t => t.IsCompleted == isCompleted.Value) 
        : todos;
    return Results.Ok(result);
})
.WithName("GetTodos");

app.MapPost("/api/todos", (TodoCreateRequest request) =>
{
    var newId = todos.Count > 0 ? todos.Max(t => t.Id) + 1 : 1;
    var item = new TodoItem(newId, request.Title, false);
    todos.Add(item);
    return Results.Created($"/api/todos/{item.Id}", item);
})
.WithName("CreateTodo");

app.Run();

record TodoItem(int Id, string Title, bool IsCompleted);
record TodoCreateRequest(string Title);