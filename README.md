```markdown
# ASP.NET Products API

Минимальный API на **ASP.NET Core 8** для управления продуктами.  
Проект демонстрирует современный подход к построению REST API с версионированием, валидацией, маппингом и soft-delete.

## Возможности

- **Версионирование API** (V1 / V2)
  - V1 — базовый набор полей
  - V2 — расширенный ответ (описание, цена в decimal, атрибуты, дата создания и т.д.)
- **CRUD** операции с продуктами
- **Soft Delete** (мягкое удаление через `DeletedAt`)
- **Валидация** запросов через FluentValidation + Endpoint Filter
- **Автоматический маппинг** Entity ↔ DTO через AutoMapper
- **Глобальная обработка ошибок** (кастомный middleware)
- **Логирование** запросов через Serilog
- **Swagger UI** с поддержкой обеих версий API
- **Health Check** эндпоинт
- **Поиск** продуктов по категории
- **CORS** (AllowAll)

## Технологии

| Технология              | Назначение                          |
|-------------------------|-------------------------------------|
| .NET 8                  | Runtime                             |
| Minimal API             | Эндпоинты                           |
| Entity Framework Core   | Работа с БД                         |
| AutoMapper              | Маппинг объектов                    |
| FluentValidation        | Валидация входных данных            |
| Serilog                 | Структурированное логирование       |
| Swashbuckle             | Swagger / OpenAPI                   |
| SQL Server              | База данных (по умолчанию)          |

## Структура проекта

```
ASP.NET-AJAX/
├── Controllers/          # (зарезервировано, используется Minimal API)
│   ├── V1/
│   └── V2/
├── Core/
│   ├── Facade/
│   └── Interface/
├── DTO/
│   ├── v1/
│   │   ├── ProductRequestV1.cs
│   │   └── ProductResponseV1.cs
│   └── v2/
│       └── ProductResponseV2.cs
├── Infrastructure/
│   ├── Entity/
│   │   └── ProductEntity.cs
│   └── Repository/
├── Mapp/
│   └── ProductMappingProfiler.cs
├── AppDbContext.cs
├── ExceptionHandleMiddleware.cs
├── IProductService.cs / ProductService.cs
├── ProductRepository.cs
├── ProductRequestValidator.cs
├── Program.cs
└── ...
```

## Запуск

### Требования

- .NET 8 SDK
- SQL Server (LocalDB / Docker / полный экземпляр)

### 1. Клонирование

```bash
git clone https://github.com/niiikkto/ASP.NET-AJAX.git
cd ASP.NET-AJAX
```

### 2. Настройка строки подключения

В `appsettings.json` или `appsettings.Development.json` добавьте:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=ProductsDb;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

### 3. Миграции (если используются)

```bash
dotnet ef database update
```

> Если миграции ещё не созданы — создайте их:
> ```bash
> dotnet ef migrations add InitialCreate
> ```

### 4. Запуск

```bash
dotnet run
```

Приложение будет доступно по адресу (порт может отличаться):

- API: `http://localhost:5197`
- Swagger UI: `http://localhost:5197/swagger`
- Health: `http://localhost:5197/health`

## Эндпоинты

### V1 — Базовая версия

| Метод  | URL                          | Описание                    |
|--------|------------------------------|-----------------------------|
| GET    | `/api/v1/products`           | Список всех продуктов       |
| GET    | `/api/v1/products/{id}`      | Продукт по ID               |
| POST   | `/api/v1/products`           | Создать продукт             |
| PUT    | `/api/v1/products/{id}`      | Обновить продукт            |
| DELETE | `/api/v1/products/{id}`      | Мягкое удаление             |

### V2 — Расширенная версия

| Метод  | URL                          | Описание                    |
|--------|------------------------------|-----------------------------|
| GET    | `/api/v2/products`           | Список (расширенный DTO)    |
| GET    | `/api/v2/products/{id}`      | Продукт по ID (расширенный) |
| POST   | `/api/v2/products`           | Создать продукт             |

### Дополнительно

| Метод  | URL                          | Описание                    |
|--------|------------------------------|-----------------------------|
| GET    | `/api/products/search?category=...` | Поиск по категории     |
| GET    | `/health`                    | Проверка работоспособности  |

## Пример запроса (создание продукта)

```http
POST /api/v1/products
Content-Type: application/json

{
  "name": "Беспроводные наушники",
  "description": "Шумоподавление, 30 часов работы",
  "price": 89.99,
  "category": "Electronics",
  "stock": 50,
  "attributes": {
    "color": "black",
    "bluetooth": "5.3"
  }
}
```

## Архитектура

- **Repository** — работа с данными (EF Core)
- **Service** — бизнес-логика
- **DTO** — контракты API (разные для V1 и V2)
- **AutoMapper** — преобразование Entity ↔ DTO
- **FluentValidation** + `ValidationFilter` — проверка входящих данных
- **ExceptionHandlingMiddleware** — единый формат ошибок
- **Soft Delete** — запись не удаляется физически, а помечается `DeletedAt`

## Лицензия

Проект создан в учебных целях.
```
