using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TechBazar.Api.Filters;

/// <summary>Runs the registered FluentValidation validator for every non-null action argument.</summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var failures = new List<ValidationFailure>();
        foreach (var arg in context.ActionArguments.Values.Where(a => a is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(arg!.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(arg), context.HttpContext.RequestAborted);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0) throw new ValidationException(failures);
        await next();
    }
}
