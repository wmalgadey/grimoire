using System.Text.Json;
using Grimoire.Api.Shared.Exceptions;

namespace Grimoire.Api.Shared.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AgentAlreadyRegisteredException ex)
        {
            await WriteErrorAsync(context, 409, "AgentAlreadyRegistered", ex.Message);
        }
        catch (AgentNotFoundException ex)
        {
            await WriteErrorAsync(context, 404, "AgentNotFound", ex.Message);
        }
        catch (InvalidStateTransitionException ex)
        {
            await WriteErrorAsync(context, 400, "InvalidStateTransition", ex.Message);
        }
        catch (DomainException ex)
        {
            _logger.LogError(ex, "Unhandled domain exception: {Message}", ex.Message);
            await WriteErrorAsync(context, 400, "DomainError", ex.Message);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Validation error: {Message}", ex.Message);
            await WriteErrorAsync(context, 400, "ValidationError", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await WriteErrorAsync(context, 500, "InternalServerError", "An unexpected error occurred. Please try again later.");
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string error, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new { error, message, statusCode });
        await context.Response.WriteAsync(body);
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();
}
