using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using TodoApi.Data;
using TodoApi.DTOs;
using TodoApi.Models;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TodosController : ControllerBase
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const string ListCacheTokenKey = "todos:list-token";

    private readonly TodoDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly IValidator<CreateTodoDto> _createValidator;
    private readonly IValidator<UpdateTodoDto> _updateValidator;
    private readonly IValidator<CompleteTodoDto> _completeValidator;

    public TodosController(
        TodoDbContext context,
        IMemoryCache cache,
        IValidator<CreateTodoDto> createValidator,
        IValidator<UpdateTodoDto> updateValidator,
        IValidator<CompleteTodoDto> completeValidator)
    {
        _context = context;
        _cache = cache;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _completeValidator = completeValidator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TodoResponseDto>>> GetTodos(
        [FromQuery] TodoStatus? status,
        [FromQuery] TodoPriority? priority,
        CancellationToken cancellationToken)
    {
        var cacheKey = ListCacheKey(status, priority);

        if (_cache.TryGetValue(cacheKey, out IEnumerable<TodoResponseDto>? cached))
            return Ok(cached);

        var query = _context.TodoItems.AsNoTracking().AsQueryable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (priority.HasValue)
            query = query.Where(t => t.Priority == priority.Value);

        var result = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TodoResponseDto(
                t.Id, t.Title, t.Description, t.Status, t.Priority, t.CreatedAt, t.DueDate,
                t.IsCompleted, t.CompletedAt))
            .ToListAsync(cancellationToken);

        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            ExpirationTokens = { new CancellationChangeToken(GetListCacheTokenSource().Token) }
        });

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TodoResponseDto>> GetTodo(int id, CancellationToken cancellationToken)
    {
        var cacheKey = ItemCacheKey(id);

        if (_cache.TryGetValue(cacheKey, out TodoResponseDto? cached))
            return Ok(cached);

        var todo = await _context.TodoItems.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (todo is null)
            return NotFound();

        var result = ToDto(todo);
        _cache.Set(cacheKey, result, CacheDuration);

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TodoResponseDto>> CreateTodo(CreateTodoDto dto, CancellationToken cancellationToken)
    {
        if (await ValidateAsync(_createValidator, dto) is { } problem)
            return problem;

        var todo = new TodoItem
        {
            Title = dto.Title,
            Description = dto.Description,
            Status = dto.Status,
            Priority = dto.Priority,
            DueDate = dto.DueDate,
            CreatedAt = DateTime.UtcNow
        };

        _context.TodoItems.Add(todo);
        await _context.SaveChangesAsync(cancellationToken);
        InvalidateListCache();

        return CreatedAtAction(nameof(GetTodo), new { id = todo.Id }, ToDto(todo));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateTodo(int id, UpdateTodoDto dto, CancellationToken cancellationToken)
    {
        if (await ValidateAsync(_updateValidator, dto) is { } problem)
            return problem;

        var todo = await _context.TodoItems.FindAsync(new object[] { id }, cancellationToken);
        if (todo is null)
            return NotFound();

        todo.Title = dto.Title;
        todo.Description = dto.Description;
        todo.Status = dto.Status;
        todo.Priority = dto.Priority;
        todo.DueDate = dto.DueDate;

        await _context.SaveChangesAsync(cancellationToken);
        _cache.Remove(ItemCacheKey(id));
        InvalidateListCache();

        return NoContent();
    }

    [HttpPatch("{id:int}/complete")]
    public async Task<ActionResult<TodoResponseDto>> CompleteTodo(int id, CancellationToken cancellationToken)
    {
        if (await ValidateAsync(_completeValidator, new CompleteTodoDto(id)) is { } problem)
            return problem;

        var todo = await _context.TodoItems.FindAsync(new object[] { id }, cancellationToken);
        if (todo is null)
            return NotFound();

        todo.IsCompleted = true;
        todo.CompletedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        _cache.Remove(ItemCacheKey(id));
        InvalidateListCache();

        return Ok(ToDto(todo));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteTodo(int id, CancellationToken cancellationToken)
    {
        var todo = await _context.TodoItems.FindAsync(new object[] { id }, cancellationToken);
        if (todo is null)
            return NotFound();

        _context.TodoItems.Remove(todo);
        await _context.SaveChangesAsync(cancellationToken);
        _cache.Remove(ItemCacheKey(id));
        InvalidateListCache();

        return NoContent();
    }

    private static string ListCacheKey(TodoStatus? status, TodoPriority? priority) => $"todos:list:{status}:{priority}";

    private static string ItemCacheKey(int id) => $"todos:{id}";

    private async Task<ActionResult?> ValidateAsync<T>(IValidator<T> validator, T dto)
    {
        var validation = await validator.ValidateAsync(dto);
        return validation.IsValid ? null : ValidationProblem(AddErrors(validation));
    }

    private CancellationTokenSource GetListCacheTokenSource()
    {
        return _cache.GetOrCreate(ListCacheTokenKey, entry =>
        {
            entry.SetPriority(CacheItemPriority.NeverRemove);
            return new CancellationTokenSource();
        })!;
    }

    private void InvalidateListCache()
    {
        if (_cache.TryGetValue(ListCacheTokenKey, out CancellationTokenSource? cts))
        {
            cts?.Cancel();
            cts?.Dispose();
        }

        _cache.Remove(ListCacheTokenKey);
    }

    private static TodoResponseDto ToDto(TodoItem todo) => new(
        todo.Id, todo.Title, todo.Description, todo.Status, todo.Priority, todo.CreatedAt, todo.DueDate,
        todo.IsCompleted, todo.CompletedAt);

    private ModelStateDictionary AddErrors(FluentValidation.Results.ValidationResult validation)
    {
        foreach (var error in validation.Errors)
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);

        return ModelState;
    }
}
