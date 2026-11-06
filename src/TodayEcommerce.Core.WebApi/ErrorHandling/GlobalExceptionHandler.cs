using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.WebApi.ErrorHandling;

/// <summary>
/// Single place that turns exceptions into HTTP responses (RFC 7807 ProblemDetails).
/// Controllers contain no try/catch: a new endpoint gets consistent error handling for free.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException ex => new ValidationProblemDetails(ex.Errors.ToDictionary(e => e.Key, e => e.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
            },
            NotFoundException ex => Problem(StatusCodes.Status404NotFound, "Not found", ex.Message),
            ConcurrencyException ex => Problem(StatusCodes.Status409Conflict, "Concurrency conflict", ex.Message),
            DomainException ex => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", ex.Message),

            // Unknown = our fault. Log everything, reveal nothing (no stack traces or SQL to the client).
            _ => Problem(StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred."),
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogInformation("Request rejected ({Status}): {Message}", problem.Status, exception.Message);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
