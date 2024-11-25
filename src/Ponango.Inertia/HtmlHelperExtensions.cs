using System.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ponango.Inertia
{
    public static class HtmlHelperExtensions
    {
        public static IHtmlContent InertiaRender(this IHtmlHelper htmlHelper, IJsonSerializerOptionBuilder serializer, string appId = "app")
        {
            if (!(htmlHelper.ViewData.Model is PageModel data))
            {
                throw new InvalidOperationException("model is not a PageModel.");
            }

            return new HtmlString($"<div id=\"{appId}\" data-page=\"{HttpUtility.HtmlEncode(data.ToJson(serializer))}\"></div>");
        }
    }
}