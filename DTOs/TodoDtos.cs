using TodoApi.Models;

namespace TodoApi.DTOs;

public record CreateTodoDto(
    string Title,
    string? Description,
    TodoStatus Status,
    TodoPriority Priority,
    DateTime? DueDate);

public record UpdateTodoDto(
    string Title,
    string? Description,
    TodoStatus Status,
    TodoPriority Priority,
    DateTime? DueDate);

public record TodoResponseDto(
    int Id,
    string Title,
    string? Description,
    TodoStatus Status,
    TodoPriority Priority,
    DateTime CreatedAt,
    DateTime? DueDate);
