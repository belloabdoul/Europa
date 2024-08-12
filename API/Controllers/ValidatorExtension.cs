using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace API.Controllers;

public static class ValidatorExtension
{
    public static void AddToModelState(this ValidationResult result, ModelStateDictionary modelState)
    {
        foreach (var error in result.Errors)
        {
            var propertyName = error.PropertyName.AsSpan();
            modelState.AddModelError(string.Concat(new Span<char>([char.ToLower(propertyName[0])]), propertyName[1..]),
                error.ErrorMessage);
        }
    }
}