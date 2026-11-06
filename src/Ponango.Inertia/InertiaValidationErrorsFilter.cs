using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ponango.Inertia;

/// <summary>
/// Keeps the ModelState errors of an Inertia form request that redirects, so the page rendered after the redirect
/// receives them in <c>props.errors</c>, like Laravel's redirect-back-with-errors. Registered globally by
/// <c>AddInertia</c> and turned off with <see cref="InertiaOptions.PersistValidationErrorsOnRedirect"/>.
/// </summary>
internal sealed class InertiaValidationErrorsFilter : IResultFilter
{
    /// <summary>
    /// The <c>HttpContext.Items</c> key of an invalid ModelState whose result was not a redirect. If the result turns
    /// out empty, <see cref="InertiaMiddleware"/> redirects back and keeps these errors then.
    /// </summary>
    internal const string PendingModelStateItemKey = "__Ponango.Inertia.PendingModelState";

    private readonly IOptions<InertiaOptions> _options;

    public InertiaValidationErrorsFilter(IOptions<InertiaOptions> options)
    {
        _options = options;
    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (!_options.Value.PersistValidationErrorsOnRedirect)
            return;

        if (context.ModelState.IsValid || HttpMethods.IsGet(context.HttpContext.Request.Method))
            return;

        var inertia = context.HttpContext.RequestServices.GetService<InertiaContext>();
        if (inertia == null || !inertia.IsInertia || inertia.IsPrecognition)
            return;

        // Every MVC redirect result implements IKeepTempDataResult (RedirectResult and InertiaController.Redirect,
        // RedirectToAction/Route/Page, LocalRedirect). Any other result may still be an empty 200 (Ok(), an empty
        // Content), which the middleware redirects back after MVC has finished, so it gets the errors to keep.
        if (context.Result is not IKeepTempDataResult)
        {
            context.HttpContext.Items[PendingModelStateItemKey] = context.ModelState;
            return;
        }

        PersistErrors(inertia, context.ModelState);
    }

    /// <summary>Keeps the ModelState errors, unless the action stored errors itself.</summary>
    internal static void PersistErrors(InertiaContext inertia, ModelStateDictionary modelState)
    {
        // Errors the action stored explicitly with FlashErrors (and their bag) win over the ModelState.
        if (inertia.FlashedErrorsInThisRequest)
            return;

        inertia.FlashErrors(modelState);
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
