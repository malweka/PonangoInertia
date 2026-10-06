using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ponango.Inertia
{
    public static class HtmlHelperExtensions
    {
        /// <summary>
        /// Renders the Inertia root element and page data script tag.
        /// V3 format: a div mount point + a script tag with JSON page data.
        /// </summary>
        public static IHtmlContent InertiaRender(this IHtmlHelper htmlHelper, IJsonSerializerOptionBuilder serializer, string appId = "app")
        {
            if (htmlHelper.ViewData.Model is not PageModel data)
            {
                throw new InvalidOperationException("model is not a PageModel.");
            }

            var json = data.ToJson(serializer);
            // The protocol requires every "/" to be escaped as "\/" so no "</script>" (in any casing) can close the
            // tag early; "<" is escaped too, as the Inertia SSR server does. Both only occur inside JSON strings, where
            // these escapes are valid. HTML entities must not be used: browsers do not decode them in a script body.
            var safeJson = json.Replace("/", "\\/").Replace("<", "\\u003c");

            return new HtmlString(
                $"<div id=\"{appId}\"></div>\n" +
                $"<script type=\"application/json\" data-page=\"{appId}\" data-inertia>\n{safeJson}\n</script>");
        }
    }
}