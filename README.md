# TaskAPI

TaskAPI - небольшой backend-сервис для управления задачами. Через API можно создать задачу, получить список задач, завершить задачу и удалить ее. При завершении задачи сервис сохраняет новое состояние в PostgreSQL, а затем публикует событие `task.completed` в RabbitMQ.

Проект сделан как ASP.NET Core Minimal API. Фокус задачи - корректный жизненный цикл задачи, валидация названия, заполнение `CompletedAt` только в момент завершения, обработка конкурентного завершения и best-effort публикация события.

## Стек

- .NET 10 / ASP.NET Core Minimal API
- Entity Framework Core
- PostgreSQL
- RabbitMQ
- Swagger / OpenAPI
- Docker Compose
- xUnit для интеграционного теста

> В исходном техническом задании указан .NET 9, но текущий проект таргетит `net10.0`.

## Возможности

- Создание задачи с названием и приоритетом.
- Получение полного списка задач без пагинации.
- Завершение задачи с установкой `CompletedAt`.
- Удаление задачи без восстановления.
- Публикация события о завершении задачи в RabbitMQ.
- Обработка повторного или конкурентного завершения через optimistic concurrency.
- Graceful shutdown RabbitMQ-публикатора с таймаутом на завершение начатых публикаций.

## Структура проекта

```text
TaskAPI/
├── TaskAPI/
│   ├── Program.cs
│   ├── TaskEndpointsExtensions.cs
│   ├── TaskItem.cs
│   ├── TasksDbContext.cs
│   ├── RabbitMqService.cs
│   ├── RabbitMqOptions.cs
│   └── TaskCompletedEvent.cs
├── TaskAPI.Tests/
│   └── TaskCompletionTests.cs
├── docker-compose.yml
├── docker-compose.override.yml
└── README.md
```

## Конфигурация

Для локального запуска через Docker Compose используется файл `.env` в корне репозитория. Он не должен коммититься, потому что содержит пароли и локальные настройки.

Пример набора переменных:

```env
POSTGRES_VERSION=latest
POSTGRES_DB=tasks
POSTGRES_USER=task_api
POSTGRES_PASSWORD=change_me
POSTGRES_PORT=15432
POSTGRES_CONNECTION_STRING=Host=postgres;Port=5432;Database=tasks;Username=task_api;Password=change_me

RABBITMQ_VERSION=3-management
RABBITMQ_DEFAULT_USER=task_api
RABBITMQ_DEFAULT_PASS=change_me
RABBITMQ_DEFAULT_VHOST=/
RABBITMQ_PORT=5673
RABBITMQ_MANAGEMENT_PORT=15673
```

Приложение ожидает:

- `ConnectionStrings__DefaultConnection` - строка подключения к PostgreSQL.
- `RabbitMq__Host` - хост RabbitMQ.
- `RabbitMq__Port` - AMQP-порт RabbitMQ.
- `RabbitMq__UserName` - пользователь RabbitMQ.
- `RabbitMq__Password` - пароль RabbitMQ.
- `RabbitMq__VirtualHost` - virtual host RabbitMQ.

## Запуск

### Через Docker Compose

```bash
docker compose up --build
```

После запуска API доступен на опубликованном Docker Compose порту приложения. Корневой маршрут `/` перенаправляет на Swagger UI:

```text
GET /
```

### Локально через .NET SDK

Поднимите PostgreSQL и RabbitMQ, затем передайте приложению настройки окружения и запустите проект:

```bash
dotnet run --project TaskAPI/TaskAPI.csproj
```

При старте приложение вызывает `EnsureCreatedAsync`, поэтому база создается автоматически, если ее еще нет.

## API

Все маршруты задач находятся в группе `/tasks`.

### Создать задачу

```http
POST /tasks
Content-Type: application/json
```

```json
{
  "title": "Buy milk",
  "priority": "High"
}
```

Правила:

- `title` обязателен, не должен быть пустым или состоять только из пробелов.
- Максимальная длина `title` - 200 символов.
- `priority` может быть `Low`, `Medium` или `High`.
- Если `priority` не передан, используется `Medium`.
- При создании `isCompleted` всегда `false`, а `completedAt` всегда `null`.

Успешный ответ:

```http
201 Created
```

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Buy milk",
  "isCompleted": false,
  "createdAt": "2026-05-07T11:00:00+00:00",
  "completedAt": null,
  "priority": "High"
}
```

Ошибки:

- `400 Bad Request`, если `title` или `priority` некорректны.

### Получить все задачи

```http
GET /tasks
```

Успешный ответ:

```http
200 OK
```

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "title": "Buy milk",
    "isCompleted": false,
    "createdAt": "2026-05-07T11:00:00+00:00",
    "completedAt": null,
    "priority": "High"
  }
]
```

### Завершить задачу

```http
PUT /tasks/{id}/complete
```

При успешном завершении сервис:

1. Находит задачу.
2. Проверяет, что она еще не завершена.
3. Устанавливает `isCompleted = true` и `completedAt`.
4. Сохраняет изменения в PostgreSQL.
5. Ставит `TaskCompletedEvent` во внутреннюю очередь публикации RabbitMQ.

Успешный ответ:

```http
200 OK
```

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Buy milk",
  "isCompleted": true,
  "createdAt": "2026-05-07T11:00:00+00:00",
  "completedAt": "2026-05-07T12:00:00+00:00",
  "priority": "High"
}
```

Ошибки:

- `404 Not Found`, если задача не найдена.
- `409 Conflict`, если задача уже завершена или другой запрос завершил ее первым.

### Удалить задачу

```http
DELETE /tasks/{id}
```

Ответы:

- `204 No Content`, если задача удалена.
- `404 Not Found`, если задача не найдена.

## Событие RabbitMQ

При завершении задачи публикуется сообщение:

- exchange: `task.events`
- routing key: `task.completed`
- content type: `application/json`

Тело сообщения:

```json
{
  "taskId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Buy milk",
  "completedAt": "2026-05-07T12:00:00Z",
  "priority": "High"
}
```

Публикация работает по принципу best effort:

- HTTP-запрос не ждет publisher confirms.
- Если RabbitMQ временно недоступен, задача остается завершенной.
- Ошибка публикации фиксируется в логах.
- При остановке приложения сервис закрывает RabbitMQ channel/connection и дает начатым публикациям до 5 секунд на завершение.

## Конкурентность

Для задачи используется `RowVersion`, настроенный как concurrency token EF Core. Если два запроса одновременно вызывают `PUT /tasks/{id}/complete`, успешным должен быть только первый. Остальные получают `409 Conflict`.

## Тесты

Запуск тестов:

```bash
dotnet test TaskAPI.slnx
```

В проекте `TaskAPI.Tests` есть интеграционный тест `CompleteTask_PublishesRabbitMqMessage`:

1. Создает задачу через API.
2. Завершает ее через API.
3. Проверяет, что сервис публикации получил `TaskCompletedEvent`.

В тесте RabbitMQ заменен заглушкой через `IRabbitMqService`, а база данных заменена на EF Core InMemory.

## Ограничения

В рамках задачи не реализованы:

- авторизация и пользователи;
- пагинация, фильтрация и сортировка;
- soft delete;
- RabbitMQ consumer;
- outbox и гарантированная доставка событий;
- retry policy для RabbitMQ;
- FluentValidation.
