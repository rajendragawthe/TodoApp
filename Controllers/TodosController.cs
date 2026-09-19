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

    public TodosController(
        TodoDbContext context,
        IMemoryCache cache,
        IValidator<CreateTodoDto> createValidator,
        IValidator<UpdateTodoDto> updateValidator)
    {
        _context = context;
        _cache = cache;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TodoResponseDto>>> GetTodos(
        [FromQuery] TodoStatus? status,
        [FromQuery] TodoPriority? priority)
    {
        var cacheKey = $"todos:list:{status}:{priority}";

        if (_cache.TryGetValue(cacheKey, out IEnumerable<TodoResponseDto>? cached))
            return Ok(cached);

        var query = _context.TodoItems.AsQueryable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (priority.HasValue)
            query = query.Where(t => t.Priority == priority.Value);

        var todos = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
        var result = todos.Select(ToDto).ToList();

        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            ExpirationTokens = { new CancellationChangeToken(GetListCacheTokenSource().Token) }
        });

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TodoResponseDto>> GetTodo(int id)
    {
        var cacheKey = $"todos:{id}";

        if (_cache.TryGetValue(cacheKey, out TodoResponseDto? cached))
            return Ok(cached);

        var todo = await _context.TodoItems.FindAsync(id);
        if (todo is null)
            return NotFound();

        var result = ToDto(todo);
        _cache.Set(cacheKey, result, CacheDuration);

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TodoResponseDto>> CreateTodo(CreateTodoDto dto)
    {
        var validation = await _createValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return ValidationProblem(AddErrors(validation));

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
        await _context.SaveChangesAsync();
        InvalidateListCache();

        return CreatedAtAction(nameof(GetTodo), new { id = todo.Id }, ToDto(todo));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateTodo(int id, UpdateTodoDto dto)
    {
        var validation = await _updateValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return ValidationProblem(AddErrors(validation));

        var todo = await _context.TodoItems.FindAsync(id);
        if (todo is null)
            return NotFound();

        todo.Title = dto.Title;
        todo.Description = dto.Description;
        todo.Status = dto.Status;
        todo.Priority = dto.Priority;
        todo.DueDate = dto.DueDate;

        await _context.SaveChangesAsync();
        _cache.Remove($"todos:{id}");
        InvalidateListCache();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteTodo(int id)
    {
        var todo = await _context.TodoItems.FindAsync(id);
        if (todo is null)
            return NotFound();

        _context.TodoItems.Remove(todo);
        await _context.SaveChangesAsync();
        _cache.Remove($"todos:{id}");
        InvalidateListCache();

        return NoContent();
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
        todo.Id, todo.Title, todo.Description, todo.Status, todo.Priority, todo.CreatedAt, todo.DueDate);

    private ModelStateDictionary AddErrors(FluentValidation.Results.ValidationResult validation)
    {
        foreach (var error in validation.Errors)
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);

        return ModelState;
    }
}
