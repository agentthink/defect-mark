namespace MaskFlow.Api.Infrastructure.Security;

public sealed class SamInternalKeyHandler : DelegatingHandler
{
    readonly string internalKey;

    public SamInternalKeyHandler()
    {
        internalKey = Environment.GetEnvironmentVariable("SAM3_INTERNAL_KEY") ?? "";
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(internalKey))
        {
            request.Headers.TryAddWithoutValidation("X-Internal-Key", internalKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
