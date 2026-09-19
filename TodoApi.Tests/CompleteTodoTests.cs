using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TodoApi.Controllers;
using TodoApi.Data;
using TodoApi.Models;
using TodoApi.Validators;
using Xunit;

namespace TodoApi.Tests;

public class CompleteTodoTests
{
    private static TodoDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TodoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static TodosController CreateController(TodoDbContext context) => new(
        context,
        new MemoryCache(new MemoryCacheOptions()),
        new CreateTodoDtoValidator(),
        new UpdateTodoDtoValidator(),
        new CompleteTodoDtoValidator());

    [Fact]
    public async Task CompleteTodo_WithExistingId_MarksCompletedAndReturnsOk()
    {
        await using var context = CreateContext();
        var todo = new TodoItem { Title = "Write tests", CreatedAt = DateTime.UtcNow };
        context.TodoItems.Add(todo);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        var result = await controller.CompleteTodo(todo.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TodoApi.DTOs.TodoResponseDto>(ok.Value);
        Assert.True(dto.IsCompleted);
        Assert.NotNull(dto.CompletedAt);

        var persisted = await context.TodoItems.FindAsync(todo.Id);
        Assert.True(persisted!.IsCompleted);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task CompleteTodo_WithIdNotGreaterThanZero_ReturnsValidationProblem()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.CompleteTodo(0, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
        Assert.IsNotType<OkObjectResult>(result.Result);
    }
}
