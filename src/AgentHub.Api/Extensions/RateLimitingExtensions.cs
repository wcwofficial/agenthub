using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace AgentHub.Api;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddAgentHubRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var partitionKey = PartitionKey(context);
                return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 240,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 4,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
            });

            options.AddPolicy("register", context =>
            {
                var partitionKey = PartitionKey(context);
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
            });
        });

        return services;
    }

    private static string PartitionKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(ip))
            return $"ip:{ip}";
        return $"cid:{context.Connection.Id}";
    }
}
