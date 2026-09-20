using Microsoft.AspNetCore.Http;

public sealed class UploadValidatorTests
{
    static FormFile CreateFile(string name, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "files", name);
    }

    [Fact]
    public async Task ValidateImageAsync_RejectsEmptyFile()
    {
        var file = CreateFile("photo.jpg", []);
        var ex = await Assert.ThrowsAsync<BadHttpRequestException>(() => UploadValidator.ValidateImageAsync(file));
        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task ValidateImageAsync_RejectsInvalidExtension()
    {
        var file = CreateFile("malware.exe", [0x4D, 0x5A, 0x90, 0x00]);
        var ex = await Assert.ThrowsAsync<BadHttpRequestException>(() => UploadValidator.ValidateImageAsync(file));
        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task ValidateImageAsync_RejectsFakeJpegHeader()
    {
        var file = CreateFile("photo.jpg", [0x00, 0x00, 0x00, 0x00]);
        var ex = await Assert.ThrowsAsync<BadHttpRequestException>(() => UploadValidator.ValidateImageAsync(file));
        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task ValidateImageAsync_AcceptsMinimalJpeg()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x00 };
        var file = CreateFile("photo.jpg", jpeg);
        await UploadValidator.ValidateImageAsync(file);
    }

    [Fact]
    public async Task ValidateImageAsync_RejectsOversizedFile()
    {
        var previous = Environment.GetEnvironmentVariable("MASKFLOW_MAX_UPLOAD_BYTES");
        Environment.SetEnvironmentVariable("MASKFLOW_MAX_UPLOAD_BYTES", "8");
        try
        {
            var file = CreateFile("photo.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 });
            var ex = await Assert.ThrowsAsync<BadHttpRequestException>(() => UploadValidator.ValidateImageAsync(file));
            Assert.Equal(StatusCodes.Status413PayloadTooLarge, ex.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MASKFLOW_MAX_UPLOAD_BYTES", previous);
        }
    }
}
