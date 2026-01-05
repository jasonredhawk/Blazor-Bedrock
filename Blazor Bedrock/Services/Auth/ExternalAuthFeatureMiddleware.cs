using Blazor_Bedrock.Services.FeatureFlag;

namespace Blazor_Bedrock.Services.Auth;

public class ExternalAuthFeatureMiddleware
{
    private readonly RequestDelegate _next;

    public ExternalAuthFeatureMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IFeatureFlagService featureFlagService)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        // Check Google authentication routes
        if (path == "/auth/external/google" || path.StartsWith("/signin-google"))
        {
            var isEnabled = await featureFlagService.IsEnabledAsync("Auth_Google");
            if (!isEnabled)
            {
                context.Response.Redirect("/auth/login?error=Google authentication is disabled");
                return;
            }
        }

        // Check Facebook authentication routes
        if (path == "/auth/external/facebook" || path.StartsWith("/signin-facebook"))
        {
            var isEnabled = await featureFlagService.IsEnabledAsync("Auth_Facebook");
            if (!isEnabled)
            {
                context.Response.Redirect("/auth/login?error=Facebook authentication is disabled");
                return;
            }
        }

        await _next(context);
    }
}
