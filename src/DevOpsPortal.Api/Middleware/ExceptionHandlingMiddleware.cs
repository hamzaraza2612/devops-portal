using System.Net;
using System.Text.Json;
using DevOpsPortal.Application.Exceptions;

namespace DevOpsPortal.Api.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (status, message) = ex switch
            {
                NotFoundException => (HttpStatusCode.NotFound, ex.Message),
                ConflictException => (HttpStatusCode.Conflict, ex.Message),
                ValidationException => (HttpStatusCode.BadRequest, ex.Message),
                AuthenticationFailedException => (HttpStatusCode.Unauthorized, ex.Message),
                _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred."),
            };

            if (status == HttpStatusCode.InternalServerError)
                logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)status;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }));
        }
    }
}
