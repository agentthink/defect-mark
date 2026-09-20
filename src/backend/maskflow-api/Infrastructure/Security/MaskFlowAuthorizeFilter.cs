using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MaskFlow.Api.Infrastructure.Security;

public sealed class MaskFlowAuthorizeFilter : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var path = context.HttpContext.Request.Path;
        if (path.StartsWithSegments("/v1") && !path.StartsWithSegments("/v1/wallet/balance"))
        {
            return Task.CompletedTask;
        }

        if (context.ActionDescriptor.EndpointMetadata.Any(metadata => metadata is IAllowAnonymous))
        {
            return Task.CompletedTask;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<MaskFlowStore>();
        if (store.OptionalUser(context.HttpContext) is not null)
        {
            return Task.CompletedTask;
        }

        context.Result = new UnauthorizedObjectResult(new { detail = "Authentication required." });
        return Task.CompletedTask;
    }
}
