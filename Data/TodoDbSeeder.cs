using TodoApi.Models;

namespace TodoApi.Data;

public static class TodoDbSeeder
{
    public static void Seed(TodoDbContext db)
    {
        if (db.TodoItems.Any())
            return;

        db.TodoItems.AddRange(
            new TodoItem
            {
                Title = "Buy groceries",
                Description = "Milk, eggs, bread",
                Status = TodoStatus.Pending,
                Priority = TodoPriority.Medium,
                DueDate = DateTime.UtcNow.Date.AddDays(2)
            },
            new TodoItem
            {
                Title = "Finish quarterly report",
                Description = "Include Q3 revenue breakdown",
                Status = TodoStatus.InProgress,
                Priority = TodoPriority.High,
                DueDate = DateTime.UtcNow.Date.AddDays(1)
            },
            new TodoItem
            {
                Title = "Schedule dentist appointment",
                Status = TodoStatus.Pending,
                Priority = TodoPriority.Low,
                DueDate = DateTime.UtcNow.Date.AddDays(14)
            },
            new TodoItem
            {
                Title = "Renew car insurance",
                Description = "Compare quotes before renewing",
                Status = TodoStatus.Pending,
                Priority = TodoPriority.High,
                DueDate = DateTime.UtcNow.Date.AddDays(5)
            },
            new TodoItem
            {
                Title = "Read 'Clean Architecture'",
                Status = TodoStatus.Completed,
                Priority = TodoPriority.Low,
                DueDate = DateTime.UtcNow.Date.AddDays(-3)
            });

        db.SaveChanges();
    }
}
