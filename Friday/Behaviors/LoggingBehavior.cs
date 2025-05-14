using System.Diagnostics;
using FluentValidation;
using Friday.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Infrastructure.FridayMediator.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly IValidator<TRequest>? _validator;

    public LoggingBehavior(
        IHttpContextAccessor httpContextAccessor,
        ILogger<LoggingBehavior<TRequest, TResponse>> logger,
        IValidator<TRequest>? validator = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _validator = validator;
    }

    public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        // var requestName = request.GetType().Name;
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        // Set user info from HTTP context if available
        var context = _httpContextAccessor.HttpContext;
        string httpMethod = string.Empty, apiEndpoint = string.Empty;
        if (context != null)
        {
            // _logger.LogInformation("Request: {@Request}", request);
            var loggedInUserId = context.Request.Headers["UserId"].FirstOrDefault() ?? string.Empty;
            var loggedInUsername = context.Request.Headers["Username"].FirstOrDefault() ?? string.Empty;
            httpMethod = context.Request.Method;
            apiEndpoint = context.Request.Path;
            if (string.IsNullOrEmpty(loggedInUserId) || string.IsNullOrEmpty(loggedInUsername))
            {
                _logger.LogWarning("UserId or Username is missing in the request headers.");
                var problemDetails = new ValidationProblemDetails
                {
                    Title = "Validation Error",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "UserId and Username are required.",
                    Errors = new Dictionary<string, string[]>()
                    {
                        { "UserId", new[] { "UserId is required." } },
                        { "Username", new[] { "Username is required." } }
                    }
                };

                throw new ValidationProblemException(problemDetails);
            }
        }

        // Validate request if validator is provided
        if (_validator != null)
        {
            var validationResult = await _validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                var errorMessage = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
                // throw new FluentValidation.ValidationException(errorMessage);

                var problemDetails = new ValidationProblemDetails
                {
                    Title = "Validation Error",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = errorMessage,
                    Errors = validationResult.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())
                };

                throw new ValidationProblemException(problemDetails);
            }
        }
    
        stopwatch.Stop();
        _logger.LogInformation("Request {0} handled in {1}ms (Method: {2}, Endpoint: {3})",
            request.GetType().Name,
            stopwatch.ElapsedMilliseconds,
            httpMethod,
            apiEndpoint);
        
        stopwatch.Reset();
        stopwatch.Start();
        var response = await next();
        stopwatch.Stop();
        // Log the response
        _logger.LogInformation("Response {0} handled in {1}ms (Method: {2}, Endpoint: {3})",
            response?.GetType().Name,
            stopwatch.ElapsedMilliseconds,
            httpMethod,
            apiEndpoint);
        return response;
    }
}

public class ValidationProblemException : Exception
{
    public ValidationProblemDetails ProblemDetails { get; }

    public ValidationProblemException(ValidationProblemDetails problemDetails)
    {
        ProblemDetails = problemDetails;
    }
}