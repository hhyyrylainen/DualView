using System.Net;

namespace DualView.Server.Middleware;

public class LocalhostOrBearerTokenMiddleware
{
    private static readonly string[] ExcludedPrefixes1 = ["/_framework", "/not-found"]; // "/_app",
    private readonly RequestDelegate next;
    private readonly ILogger<LocalhostOrBearerTokenMiddleware> logger;

    public LocalhostOrBearerTokenMiddleware(RequestDelegate next, ILogger<LocalhostOrBearerTokenMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Allow static assets and some other stuff
        if (IsExcludedPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        // Check if the request is from localhost
        if (IsLocalhost(context))
        {
            await next(context);
            return;
        }

        // Check for valid bearer token
        if (await HasValidBearerToken(context))
        {
            await next(context);
            return;
        }

        // Request is not authorized
        logger.LogWarning("Unauthorized request from {IpAddress}", context.Connection.RemoteIpAddress);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync(
            "Unauthorized: requests must originate from localhost or include a valid bearer token");
    }

    private static bool IsLocalhost(HttpContext context)
    {
        var remoteIpAddress = context.Connection.RemoteIpAddress;
        if (remoteIpAddress == null)
            return false;

        // Check for loopback addresses
        return IPAddress.IsLoopback(remoteIpAddress);
    }

    private static Task<bool> HasValidBearerToken(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(false);

        var token = authHeader["Bearer ".Length..];

        // TODO: Validate token from database
        // var databaseService = context.RequestServices.GetRequiredService<IDatabaseService>();
        // return await databaseService.IsValidTokenAsync(token);

        return Task.FromResult(false); // For now, always reject until you implement database validation
    }

    private static bool IsExcludedPath(PathString path)
    {
        // Exclude paths that should always be accessible
        return ExcludedPrefixes1.Any(prefix => path.StartsWithSegments(prefix));
    }
}
