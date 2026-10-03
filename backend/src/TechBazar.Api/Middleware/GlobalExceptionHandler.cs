using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TechBazar.Application.Common;

namespace TechBazar.Api.Middleware;

/// <summary>Maps exceptions to RFC 7807 ProblemDetails. Internal details are only exposed in Development.</summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment env,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499; // client closed request
            return true;
        }

        ProblemDetails problem;
        switch (exception)
        {
            case ValidationException ve:
                var errors = ve.Errors
                    .GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? "request" : ToCamel(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
                problem = new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest, Title = "One or more validation errors occurred.",
                };
                break;
            case NotFoundException:
                problem = Create(StatusCodes.Status404NotFound, "Resource not found.", exception.Message);
                break;
            case ConflictException:
                problem = Create(StatusCodes.Status409Conflict, "Conflict.", exception.Message);
                break;
            case InvalidTokenException:
                // deliberately the same answer for unknown / expired / used / wrong-purpose links
                problem = Create(StatusCodes.Status400BadRequest, "Invalid or expired link.", "This link is invalid or has expired. Please request a new one.");
                break;
            case EmailNotVerifiedException:
                problem = Create(StatusCodes.Status403Forbidden, "Email not verified.", "Please verify your email address to continue. Check your inbox or request a new link.");
                problem.Extensions["code"] = "email_not_verified";   // lets the UI show the right prompt without parsing text
                break;
            case AuthenticationFailedException:
                problem = Create(StatusCodes.Status401Unauthorized, "Authentication failed.", exception.Message);
                break;
            case BadHttpRequestException bad:
                problem = Create(StatusCodes.Status400BadRequest, "Bad request.", bad.Message);
                break;
            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
                problem = Create(StatusCodes.Status500InternalServerError, "An unexpected error occurred.",
                    env.IsDevelopment() ? exception.ToString() : "Please try again later or contact support with the trace id.");
                break;
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context, ProblemDetails = problem, Exception = exception,
        });
    }

    private static ProblemDetails Create(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };

    private static string ToCamel(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
