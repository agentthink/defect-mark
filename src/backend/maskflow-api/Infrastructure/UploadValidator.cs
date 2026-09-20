public static class UploadValidator
{
    static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"
    };

    static long MaxImageBytes
    {
        get
        {
            if (long.TryParse(Environment.GetEnvironmentVariable("MASKFLOW_MAX_UPLOAD_BYTES"), out var value) && value > 0)
            {
                return value;
            }

            return 50L * 1024 * 1024;
        }
    }

    public static async Task ValidateImageAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
        {
            throw new BadHttpRequestException("Empty file.", StatusCodes.Status400BadRequest);
        }

        if (file.Length > MaxImageBytes)
        {
            throw new BadHttpRequestException($"File too large. Maximum size is {MaxImageBytes / (1024 * 1024)} MB.", StatusCodes.Status413PayloadTooLarge);
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            throw new BadHttpRequestException("Unsupported image type. Allowed: JPG, PNG, WebP, BMP, GIF.", StatusCodes.Status400BadRequest);
        }

        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        if (read < 3 || !IsKnownImageHeader(header.AsSpan(0, read)))
        {
            throw new BadHttpRequestException("Invalid image content.", StatusCodes.Status400BadRequest);
        }
    }

    static bool IsKnownImageHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return true;
        }

        if (header.Length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return true;
        }

        if (header.Length >= 6
            && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38
            && (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
        {
            return true;
        }

        if (header.Length >= 2 && header[0] == 0x42 && header[1] == 0x4D)
        {
            return true;
        }

        if (header.Length >= 12
            && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return true;
        }

        return false;
    }
}
