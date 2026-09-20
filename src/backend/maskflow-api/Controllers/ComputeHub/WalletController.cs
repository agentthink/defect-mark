using Microsoft.AspNetCore.Mvc;

[Tags("Wallet")]
public sealed class WalletController : ControllerBase
{
    private readonly MaskFlowStore store;

    public WalletController(MaskFlowStore store)
    {
        this.store = store;
    }

    [HttpGet("/v1/wallet/balance")]
    public IActionResult Balance()
    {
        var user = store.RequireUser(HttpContext);
        var balance = store.State.WalletLedger.Where(x => x.UserId == user.Id).Sum(x => x.Delta);
        return Ok(new { userId = user.Id, balance });
    }
}