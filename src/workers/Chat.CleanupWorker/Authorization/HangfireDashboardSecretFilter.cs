using System.Security.Cryptography;
using System.Text;

using Hangfire.Dashboard;

using Microsoft.AspNetCore.Http;

namespace Chat.CleanupWorker.Authorization;

internal sealed class HangfireDashboardSecretFilter(string expectedSecret) : IDashboardAsyncAuthorizationFilter
{
    public const string SecretConfigurationKey = "HangfireDashboard:Secret";
    public const string SecretQueryParameterName = "secret";

    private const string CookieName = "__Nova-hangfire-dashboard";

    public Task<bool> AuthorizeAsync(DashboardContext context)
    {
        HttpContext httpContext = context.GetHttpContext();

        string? provided = GetProvidedSecret(httpContext);

        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expectedSecret))
        {
            return Task.FromResult(false);
        }

        bool authorized = CryptographicOperations.FixedTimeEquals
        (
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expectedSecret)
        );

        if (authorized && httpContext.Request.Query.ContainsKey(SecretQueryParameterName))
        {
            httpContext.Response.Cookies.Append
            (
                CookieName,
                provided,
#pragma warning disable S2092
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = httpContext.Request.IsHttps,
                    MaxAge = TimeSpan.FromHours(8)
                }
#pragma warning restore S2092
            );
        }

        return Task.FromResult(authorized);
    }

    private static string? GetProvidedSecret(HttpContext httpContext)
    {
        string? querySecret = httpContext.Request.Query[SecretQueryParameterName];

        if (!string.IsNullOrEmpty(querySecret))
        {
            return querySecret;
        }

        return httpContext.Request.Cookies[CookieName];
    }
}