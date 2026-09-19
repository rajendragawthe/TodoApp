---
name: add-endpoint
description: Use when adding a new API endpoint (a new controller action, or a whole new resource controller) to this ASP.NET Core Todo API. Enforces this repo's exact conventions - DTO records, FluentValidation validators, manual validate-then-ValidationProblem pattern, and an xUnit test covering the happy path plus a validation failure.
---

# Adding an endpoint to TodoApi

This project is controller-based ASP.NET Core on .NET 10 with EF Core/SQLite. It does **not**
use `FluentValidation.AspNetCore` auto-validation or a validation middleware pipeline - validation
is manual, per-action. Follow the exact pattern below; don't introduce a different validation
mechanism (filters, attributes, auto-validation) even if it seems more idiomatic elsewhere.

Reference implementation to imitate: `Controllers/TodosController.cs`, `DTOs/TodoDtos.cs`,
`Validators/TodoDtoValidators.cs`.

## Steps

### 1. DTO record(s) in `DTOs/`

- Add to the existing `DTOs/<Entity>Dtos.cs` file if the entity already has one (e.g. new action
  on `TodoItem` goes in `DTOs/TodoDtos.cs`), otherwise create `DTOs/<Entity>Dtos.cs`.
- Plain C# `record`s only. **No DataAnnotations attributes** - validation is FluentValidation-only.
- Naming: `Create<Entity>Dto`, `Update<Entity>Dto`, `<Entity>ResponseDto` (match whichever the new
  action needs). Controllers never expose the EF entity type directly - always map to/from a
  response DTO.

### 2. Validator in `Validators/`

- Add to the existing `Validators/<Entity>DtoValidators.cs` file, or create it if new.
- One `public class <Dto>Validator : AbstractValidator<TDto>` per DTO that needs validation, with
  rules built in the constructor via `RuleFor(...)`, e.g.:

```csharp
public class CreateWidgetDtoValidator : AbstractValidator<CreateWidgetDto>
{
    public CreateWidgetDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
```

### 3. Validator registration

- Nothing to add by hand in the common case: `Program.cs` already calls
  `builder.Services.AddValidatorsFromAssemblyContaining<Program>();`, which scans the assembly and
  picks up any new `AbstractValidator<T>` automatically.
- Only touch `Program.cs` if that line is missing (it shouldn't be) - if so, add it next to the
  other `builder.Services.Add*` calls, before `builder.Build()`.

### 4. Controller action - manual validation, no middleware

- There is no validation middleware/filter pipeline in this project and you must not add one.
  Each action resolves its validator via constructor injection and validates explicitly:

```csharp
private readonly IValidator<CreateWidgetDto> _createWidgetValidator;

public WidgetsController(IValidator<CreateWidgetDto> createWidgetValidator /*, ... */)
{
    _createWidgetValidator = createWidgetValidator;
}

[HttpPost]
public async Task<ActionResult<WidgetResponseDto>> CreateWidget(CreateWidgetDto dto)
{
    var validation = await _createWidgetValidator.ValidateAsync(dto);
    if (!validation.IsValid)
        return ValidationProblem(AddErrors(validation));

    // ... map dto -> entity, save via DbContext, map entity -> response DTO ...

    return CreatedAtAction(nameof(GetWidget), new { id = widget.Id }, ToDto(widget));
}
```

  where `AddErrors` converts `FluentValidation.Results.ValidationResult` into `ModelState`
  (copy the private helper from `TodosController.AddErrors` verbatim into the new controller, or
  reuse it if the action is added to `TodosController` itself).
- If the endpoint reads data that's already cached in `TodosController` (list/by-id lookups use
  `IMemoryCache` with a 5-minute `CacheDuration` and a cancellation-token-based list invalidation
  scheme), follow that same caching pattern for symmetry, and call `InvalidateListCache()` /
  `_cache.Remove($"todos:{id}")` (or the equivalent for the new entity) after any write.
- Route via `[ApiController]` + `[Route("api/[controller]")]` on the controller class; per-action
  attributes are `[HttpGet]`, `[HttpGet("{id:int}")]`, `[HttpPost]`, `[HttpPut("{id:int}")]`,
  `[HttpDelete("{id:int}")]` as appropriate - constrain route id params with `:int`.

### 5. xUnit test

No test project exists yet in this repo. If this is the first endpoint test being added, bootstrap
one first:

```bash
dotnet new xunit -n TodoApi.Tests
dotnet add TodoApi.Tests/TodoApi.Tests.csproj reference TodoApi.csproj
dotnet add TodoApi.Tests/TodoApi.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory
```

Then write tests that call the controller action directly (no HTTP host needed):

- Build a `TodoDbContext` using the EF Core **InMemory** provider with a unique database name per
  test (e.g. `Guid.NewGuid().ToString()`), so tests don't share state.
- Use a real `AbstractValidator<T>` instance (e.g. `new CreateWidgetDtoValidator()`) rather than a
  mock - the validators have no external dependencies, so there's no reason to fake them.
- Use a real `new MemoryCache(new MemoryCacheOptions())` if the controller under test takes
  `IMemoryCache`.
- Cover at minimum:
  1. **Happy path** - valid DTO produces the expected success result (e.g. `CreatedAtActionResult`
     / `OkObjectResult`) and persists/reflects the change via the DbContext.
  2. **Validation failure** - an invalid DTO (e.g. empty required field) returns
     `ValidationProblem`-shaped `ActionResult` and the entity is **not** persisted.

Example shape, modeled on how `TodosController` is constructed:

```csharp
public class WidgetsControllerTests
{
    private static TodoDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TodoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task CreateWidget_WithValidDto_ReturnsCreated()
    {
        await using var context = CreateContext();
        var controller = new WidgetsController(context, new MemoryCache(new MemoryCacheOptions()),
            new CreateWidgetDtoValidator());

        var result = await controller.CreateWidget(new CreateWidgetDto("Valid name"));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Single(context.Widgets);
    }

    [Fact]
    public async Task CreateWidget_WithEmptyName_ReturnsValidationProblem()
    {
        await using var context = CreateContext();
        var controller = new WidgetsController(context, new MemoryCache(new MemoryCacheOptions()),
            new CreateWidgetDtoValidator());

        var result = await controller.CreateWidget(new CreateWidgetDto(""));

        Assert.IsType<ObjectResult>(result.Result);
        Assert.Empty(context.Widgets);
    }
}
```

Run with `dotnet test`.

## Checklist before calling the endpoint done

- [ ] DTO(s) added as records, no DataAnnotations
- [ ] Validator(s) added under `Validators/`, one `AbstractValidator<T>` per DTO
- [ ] `AddValidatorsFromAssemblyContaining<Program>()` confirmed present in `Program.cs`
- [ ] Controller action manually validates via injected `IValidator<T>` and returns
      `ValidationProblem(AddErrors(validation))` on failure - no auto-validation, no filter
- [ ] Caching pattern followed/invalidated if the endpoint touches cached data
- [ ] xUnit tests added covering happy path + at least one validation failure, `dotnet test` passes
