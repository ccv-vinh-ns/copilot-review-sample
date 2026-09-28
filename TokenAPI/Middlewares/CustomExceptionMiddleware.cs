using TokenAPI.Common.Extensions;

namespace TokenAPI.Middlewares
{
    public class CustomExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CustomExceptionMiddleware> _logger;

        public CustomExceptionMiddleware(RequestDelegate next, ILogger<CustomExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context); // Continue to the next middleware
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception occurred.");
                await HandleExceptionAsync(context, ex);
            }
        }

        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            if (exception is CustomUnauthorizedException unauthorizedException)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return context.Response.WriteAsJsonAsync(unauthorizedException.ErrorDetails);
            }
            else if (exception is CustomBadRequestException badRequestException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return context.Response.WriteAsJsonAsync(badRequestException.ErrorDetails);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return context.Response.WriteAsJsonAsync(new
                {
                    code = 500,
                    timestamp = DateTime.UtcNow.ToString(),
                    path = context.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                    error = new
                    {
                        code = "INTERNAL_SERVER_EXCEPTION",
                        message = exception.Message
                    }
                });
            }
        }
    }
}
