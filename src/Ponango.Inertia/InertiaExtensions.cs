using System.Text;
using Microsoft.AspNetCore.Http;

namespace Ponango.Inertia
{
    public static class InertiaExtensions
    {
        public static string CreateMd5Hash(string input)
        {
            // Use input string to calculate MD5 hash
            using System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            // Convert the byte array to hexadecimal string prior to .NET 5
            var sb = new StringBuilder();
            for (var i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }

            return sb.ToString().ToLower();
        }

        public static InertiaResult Inertia<T>(this InertiaContext context, string viewName,  T model, string component, string propsName = "data")
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                component = typeof(T).Name;
            }

            var result = new InertiaResult(component, context.AssetVersionProvider.GetAssetVersion())
            {
                ViewName = viewName,
                Url = context.Request?.Path,
                InertiaContext = context
            };

            if (model != null)
            {
                result.Props.Add(propsName, model);
            }
            else
            {
                result.Props.Add(propsName, new { });
            }

            return result;
        }
    }
}