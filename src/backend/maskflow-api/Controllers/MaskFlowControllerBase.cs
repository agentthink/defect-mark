using Microsoft.AspNetCore.Mvc;

[ApiController]
public abstract class MaskFlowControllerBase : ControllerBase
{
    protected readonly MaskFlowStore Store;

    protected MaskFlowControllerBase(MaskFlowStore store)
    {
        Store = store;
    }

    protected User CurrentUser() => Store.RequireUser(HttpContext);
}