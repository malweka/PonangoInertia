using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
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
    private readonly IOptions<InertiaOptions> _options;

    public InertiaValidationErrorsFilter(IOptions<InertiaOptions> options)
    {
        _options = options;
    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (!_options.Value.PersistValidationErrorsOnRedirect)
            return;

        // Every MVC redirect result implements IKeepTempDataResult (RedirectResult and InertiaController.Redirect,
        // RedirectToAction/Route/Page, LocalRedirect).
        if (context.ModelState.IsValid || context.Result is not IKeepTempDataResult)
            return;

        if (HttpMethods.IsGet(context.HttpContext.Request.Method))
            return;

        var inertia = context.HttpContext.RequestServices.GetService<InertiaContext>();
        if (inertia == null || !inertia.IsInertia || inertia.IsPrecognition)
            return;

        // Errors the action stored explicitly with FlashErrors (and their bag) win over the ModelState.
        if (inertia.FlashedErrorsInThisRequest)
            return;

        inertia.FlashErrors(context.ModelState);
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
