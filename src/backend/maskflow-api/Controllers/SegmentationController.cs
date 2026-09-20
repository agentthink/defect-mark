using System.Net.Http.Headers;
using MaskFlow.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

[Tags("Segmentation")]
public sealed class SegmentationController : ControllerBase
{
    private readonly MaskFlowStore store;
    private readonly IHttpClientFactory clientFactory;
    private readonly SamInferenceGate samGate;

    public SegmentationController(MaskFlowStore store, IHttpClientFactory clientFactory, SamInferenceGate samGate)
    {
        this.store = store;
        this.clientFactory = clientFactory;
        this.samGate = samGate;
    }

    [HttpGet("/api/segment/status")]
    public async Task<IActionResult> Status()
    {
        store.RequireUser(HttpContext);
        return await ForwardGetAsync("/api/status");
    }

    [HttpPost("/api/segment")]
    public async Task<IActionResult> Segment()
    {
        var user = store.RequireUser(HttpContext);
        try
        {
            store.EnsureAiQuotaAvailable(user);
        }
        catch (BadHttpRequestException ex)
        {
            return StatusCode(ex.StatusCode, new { detail = ex.Message });
        }

        await using var slot = await samGate.TryAcquireAsync(HttpContext.RequestAborted);
        if (slot is null)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { detail = "Too many concurrent AI requests. Try again shortly." });
        }

        var result = await ForwardMultipartAsync("/api/segment");
        if (result.Success) await store.ConsumeAiQuotaAsync(user.Id, 1);
        return result.Response;
    }

    [HttpPost("/api/segment/points")]
    public async Task<IActionResult> SegmentPoints()
    {
        var user = store.RequireUser(HttpContext);
        try
        {
            store.EnsureAiQuotaAvailable(user);
        }
        catch (BadHttpRequestException ex)
        {
            return StatusCode(ex.StatusCode, new { detail = ex.Message });
        }

        await using var slot = await samGate.TryAcquireAsync(HttpContext.RequestAborted);
        if (slot is null)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { detail = "Too many concurrent AI requests. Try again shortly." });
        }

        var result = await ForwardMultipartAsync("/api/segment/points");
        if (result.Success) await store.ConsumeAiQuotaAsync(user.Id, 1);
        return result.Response;
    }

    private async Task<IActionResult> ForwardGetAsync(string path)
    {
        try
        {
            var client = clientFactory.CreateClient("sam");
            var response = await client.GetAsync(path, HttpContext.RequestAborted);
            return await ProxyResponse(response);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(503, new { detail = $"SAM inference service is unavailable: {ex.Message}" });
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, new { detail = "SAM inference service request timed out." });
        }
    }

    private async Task<(IActionResult Response, bool Success)> ForwardMultipartAsync(string path)
    {
        if (!Request.HasFormContentType) return (BadRequest(new { detail = "Multipart form data is required." }), false);
        var form = await Request.ReadFormAsync();
        if (form.Files.Count == 0) return (BadRequest(new { detail = "Image file is required." }), false);

        foreach (var file in form.Files)
        {
            try
            {
                await UploadValidator.ValidateImageAsync(file, HttpContext.RequestAborted);
            }
            catch (BadHttpRequestException ex)
            {
                return (StatusCode(ex.StatusCode, new { detail = ex.Message }), false);
            }
        }

        using var content = new MultipartFormDataContent();
        foreach (var field in form)
        {
            foreach (var value in field.Value)
            {
                content.Add(new StringContent(value ?? ""), field.Key);
            }
        }

        foreach (var file in form.Files)
        {
            var stream = file.OpenReadStream();
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType ?? "application/octet-stream");
            content.Add(fileContent, file.Name, file.FileName);
        }

        try
        {
            var client = clientFactory.CreateClient("sam");
            var response = await client.PostAsync(path, content, HttpContext.RequestAborted);
            return (await ProxyResponse(response), response.IsSuccessStatusCode);
        }
        catch (HttpRequestException ex)
        {
            return (StatusCode(503, new { detail = $"SAM inference service is unavailable: {ex.Message}" }), false);
        }
        catch (TaskCanceledException)
        {
            return (StatusCode(504, new { detail = "SAM inference service request timed out." }), false);
        }
    }

    private async Task<IActionResult> ProxyResponse(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
        return new ContentResult
        {
            Content = body,
            ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
            StatusCode = (int)response.StatusCode
        };
    }
}
