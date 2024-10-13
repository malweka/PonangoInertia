using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ponango.Inertia
{
    class InertiaRedirectResult : RedirectResult
    {
        public InertiaRedirectResult(string url) : base(url)
        {

        }

        public override async Task ExecuteResultAsync(ActionContext context)
        {
            if (Permanent || PreserveMethod || context.HttpContext.Request.Method.ToUpper().Equals("GET"))
            {
                await base.ExecuteResultAsync(context);
                return;
            }

            IServiceProvider serviceProvider = context.HttpContext.RequestServices;
            IUrlHelperFactory urlHelpFactory = serviceProvider.GetRequiredService<IUrlHelperFactory>();
            IUrlHelper urlHelper = UrlHelper ?? urlHelpFactory.GetUrlHelper(context);

            string redirectUri = Url;
            if (urlHelper.IsLocalUrl(redirectUri))
            {
                redirectUri = $"{context.HttpContext.Request.Scheme}://{context.HttpContext.Request.Host}{urlHelper.Content(Url)}";
            }

            context.HttpContext.Response.StatusCode = 303;
            context.HttpContext.Response.Headers["Location"] = redirectUri;

        }
    }
}