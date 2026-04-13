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
            // Escape </script> within JSON to prevent premature tag closure
            var safeJson = json.Replace("</script>", "<\\/script>");

            return new HtmlString(
                $"<div id=\"{appId}\"></div>\n" +
                $"<script type=\"application/json\" data-page data-inertia>\n{safeJson}\n</script>");
        }
    }
}