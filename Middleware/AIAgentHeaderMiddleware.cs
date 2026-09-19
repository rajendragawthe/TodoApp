namespace TodoApi.Middleware;

public class AIAgentHeaderMiddleware
{
    private readonly RequestDelegate _next;

    public AIAgentHeaderMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["AIAgent"] = "claudecode";
            context.Response.Headers["X-Powered-By"] = "Claude-AI-Agent";
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
