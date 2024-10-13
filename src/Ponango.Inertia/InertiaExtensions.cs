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

        public static InertiaResult Inertia<T>(this InertiaContext context,string viewName, T model, string? component = null,
            string? assetVersion = null)
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                component = typeof(T).Name;
            }

            if (string.IsNullOrWhiteSpace(assetVersion))
            {
                var version = typeof(T).Assembly.GetName().Version;
                assetVersion = InertiaExtensions.CreateMd5Hash(version != null ? version.ToString() : "1.0.0");
            }

            return new InertiaResult(component, assetVersion)
            {
                ViewName = viewName,
                Url = context.Request?.Path
            };
        }
    }
}