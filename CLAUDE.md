# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                          # build
dotnet run --urls "http://localhost:5299"   # run the API (port is not fixed in launchSettings, pass explicitly)
dotnet ef migrations add <Name>       # add a migration after changing Models/TodoItem.cs or TodoDbContext
dotnet ef database update             # apply pending migrations to todo.db
```

No test project exists yet. `dotnet-ef` must be installed globally (`dotnet tool install --global dotnet-ef`) to run migration commands.

## Architecture

ASP.NET Core Web API (controller-based, not minimal APIs) on .NET 10, backed by SQLite via EF Core.

- `Models/TodoItem.cs` — the single entity, with `TodoStatus` and `TodoPriority` enums stored as integers in SQLite.
- `Data/TodoDbContext.cs` — EF Core context, one `DbSet<TodoItem>`.
- `DTOs/TodoDtos.cs` — `CreateTodoDto` / `UpdateTodoDto` / `TodoResponseDto` records. Controllers never expose `TodoItem` directly. No DataAnnotations on these — validation is FluentValidation-only (see below).
- `Validators/TodoDtoValidators.cs` — `CreateTodoDtoValidator` / `UpdateTodoDtoValidator`, registered via `AddValidatorsFromAssemblyContaining<Program>()` in `Program.cs`. There is no `FluentValidation.AspNetCore` auto-validation pipeline wired up (that package is unmaintained) — each controller action resolves its `IValidator<T>` via constructor injection and calls `ValidateAsync` explicitly, converting failures to a `ValidationProblem` via `ModelState`. Any new endpoint taking a validated DTO must follow this same manual-validate-then-`ValidationProblem` pattern; it will not happen automatically.
- `Controllers/TodosController.cs` — full CRUD at `api/todos`, with optional `status`/`priority` query filters on the list endpoint.
- `Data/TodoDbSeeder.cs` — `TodoDbSeeder.Seed(db)` inserts 5 sample todos, but only if `TodoItems` is empty (safe to call on every startup). Called from `Program.cs` only when `app.Environment.IsDevelopment()`.
- `Program.cs` — registers `TodoDbContext` with the `DefaultConnection` string from `appsettings.json`, calls `db.Database.Migrate()` on startup so the SQLite schema is always current when the app launches (no separate migration step needed in dev), then seeds dev data.
- `Middleware/AIAgentHeaderMiddleware.cs` — adds an `AIAgent: claudecode` header to every response. Registered via `app.UseMiddleware<AIAgentHeaderMiddleware>()` before `UseHttpsRedirection`, so it applies globally, including to error responses.

The `SQLitePCLRaw.bundle_e_sqlite3` package is pinned to `3.0.3` explicitly — the version EF Core 10's Sqlite provider pulls transitively (`2.1.11`) has a known high-severity CVE (GHSA-2m69-gcr7-jv3q). Keep this override when upgrading `Microsoft.EntityFrameworkCore.Sqlite`.
