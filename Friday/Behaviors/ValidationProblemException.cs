namespace Infrastructure.FridayMediator.Behaviors;

public class ExceptionLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionLoggingMiddleware> _logger;

    public ExceptionLoggingMiddleware(RequestDelegate next, ILogger<ExceptionLoggingMiddleware> logger)
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
        catch (ValidationProblemException ex)
        {
            _logger.LogError(ex, "Validation error occurred.");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = ex.ProblemDetails;
            await context.Response.WriteAsJsonAsync(problemDetails);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            
            await context.Response.WriteAsJsonAsync(new { title = ex.Message, 
                status = StatusCodes.Status400BadRequest, 
                detail = ex.InnerException,  
                errors = new Dictionary<string, string[]>()
                {
                    { "Error", new[] { ex.Message } }
                }
            }) ;        
        }
    }
}