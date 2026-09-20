using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MaskFlow.Api.Infrastructure.Security;

public sealed class AdminApiKeyFilter : IAsyncAuthorizationFilter
{
    static bool FixedTimeEquals(string provided, string expected)
    {
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var path = context.HttpContext.Request.Path;
        if (!path.StartsWithSegments("/v1"))
        {
            return Task.CompletedTask;
        }

        if (path.StartsWithSegments("/v1/wallet/balance"))
        {
            return Task.CompletedTask;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<MaskFlowStore>();
        var expectedAdmin = Environment.GetEnvironmentVariable("MASKFLOW_ADMIN_API_KEY");
        var adminKey = context.HttpContext.Request.Headers["X-Admin-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(expectedAdmin) && FixedTimeEquals(adminKey, expectedAdmin))
        {
            context.HttpContext.Items[MaskFlowHttpItems.AdminAccess] = true;
            return Task.CompletedTask;
        }

        if (IsUserScopedJobEndpoint(path, context.HttpContext.Request.Method)
            && store.OptionalUser(context.HttpContext) is User user)
        {
            context.HttpContext.Items[MaskFlowHttpItems.AuthenticatedUser] = user;
            return Task.CompletedTask;
        }

        if (TryGetNodeId(context.HttpContext, out var nodeId))
        {
            var nodeKey = context.HttpContext.Request.Headers["X-Node-Key"].ToString();
            if (store.ValidateNodeKey(nodeId, nodeKey))
            {
                context.HttpContext.Items[MaskFlowHttpItems.NodeId] = nodeId;
                return Task.CompletedTask;
            }
        }

        if (string.IsNullOrWhiteSpace(expectedAdmin))
        {
            context.Result = new ObjectResult(new { detail = "ComputeHub is disabled. Set MASKFLOW_ADMIN_API_KEY." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return Task.CompletedTask;
        }

        context.Result = new UnauthorizedObjectResult(new { detail = "Admin or node key required." });
        return Task.CompletedTask;
    }

    static bool TryGetNodeId(HttpContext context, out string nodeId)
    {
        nodeId = "";
        var path = context.Request.Path;
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length < 2 || !segments[0].Equals("v1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (segments.Length >= 4
            && segments[1].Equals("nodes", StringComparison.OrdinalIgnoreCase)
            && segments[3].Equals("heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            nodeId = segments[2];
            return !string.IsNullOrWhiteSpace(nodeId);
        }

        if (segments.Length >= 5
            && segments[1].Equals("nodes", StringComparison.OrdinalIgnoreCase)
            && segments[3].Equals("jobs", StringComparison.OrdinalIgnoreCase)
            && segments[4].Equals("poll", StringComparison.OrdinalIgnoreCase))
        {
            nodeId = segments[2];
            return !string.IsNullOrWhiteSpace(nodeId);
        }

        if (segments.Length >= 4
            && segments[1].Equals("jobs", StringComparison.OrdinalIgnoreCase)
            && segments[3].Equals("events", StringComparison.OrdinalIgnoreCase))
        {
            nodeId = context.Request.Headers["X-Node-Id"].ToString();
            return !string.IsNullOrWhiteSpace(nodeId);
        }

        if (segments.Length >= 4
            && segments[1].Equals("jobs", StringComparison.OrdinalIgnoreCase)
            && segments[3].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            nodeId = context.Request.Headers["X-Node-Id"].ToString();
            return !string.IsNullOrWhiteSpace(nodeId);
        }

        return false;
    }

    static bool IsUserScopedJobEndpoint(PathString path, string method)
    {
        if (!path.StartsWithSegments("/v1/jobs"))
        {
            return false;
        }

        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length == 2)
        {
            return method is "GET" or "POST";
        }

        if (segments.Length == 3)
        {
            return method == "GET";
        }

        return false;
    }
}
