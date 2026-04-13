using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Ponango.Inertia;

/// <summary>
/// Marks a controller action as supporting Precognition validation.
///
/// When the client sends a Precognition: true request, this filter short-circuits
/// before the controller action executes:
///   - If validation passed  → 204 No Content  + Precognition: true, Precognition-Success: true
///   - If validation failed  → 422 Unprocessable Entity + Precognition: true + errors JSON body
///
/// The filter also honours Precognition-Validate-Only by removing all other model-state errors
/// after model binding/validation and before the action runs, so only the requested fields
/// participate in the final validation result.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class PrecognitiveAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var inertiaContext = context.HttpContext.RequestServices.GetRequiredService<InertiaContext>();
        if (!inertiaContext.IsPrecognition) return;

        // If client only wants specific fields validated, remove all other ModelState keys
        var validateOnly = inertiaContext.Headers.PrecognitionValidateOnly;
        if (!string.IsNullOrEmpty(validateOnly))
        {
            var fields = validateOnly
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var key in context.ModelState.Keys.Where(k => !fields.Contains(k)).ToList())
                context.ModelState.Remove(key);
        }

        context.HttpContext.Response.Headers["Precognition"] = "true";

        if (context.ModelState.IsValid)
        {
            // Validation passed — short-circuit before the action can execute any side effects.
            context.Result = new StatusCodeResult(204);
            context.HttpContext.Response.Headers["Precognition-Success"] = "true";
        }
        else
        {
            // Validation failed — short-circuit with a flat JSON error payload.
            var errors = ExtractErrors(context.ModelState);
            var jsonOptions = context.HttpContext.RequestServices
                .GetService<IJsonSerializerOptionBuilder>();

            var serializerOptions = new JsonSerializerOptions();
            jsonOptions?.SetSerializerOptions(serializerOptions);

            context.Result = new ContentResult
            {
                StatusCode = 422,
                ContentType = "application/json",
                Content = JsonSerializer.Serialize(new { errors }, serializerOptions)
            };
        }
    }

    static Dictionary<string, string> ExtractErrors(ModelStateDictionary modelState)
    {
        var errors = new Dictionary<string, string>();
        foreach (var key in modelState.Keys)
        {
            var entry = modelState[key];
            if (entry?.Errors.Count > 0)
                errors[key] = entry.Errors[0].ErrorMessage;
        }
        return errors;
    }
}
