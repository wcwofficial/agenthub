namespace AgentHub.Api;

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseAgentHubSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
            ctx.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
            ctx.Response.Headers.TryAdd("X-Frame-Options", "DENY");
            await next();
        });
    }
}
