namespace MaskFlow.Api.Infrastructure.Security;

public static class ProductionSecretsValidator
{
    static readonly string[] DeniedFragments =
    {
        "change-me",
        "replace-me",
        "example",
        "local-development",
        "maskflow-sam-dev-key",
        "maskflow-admin-dev-key",
        "password=maskflow;"
    };

    public static void EnsureConfigured(IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        RequireSecret("SAM3_INTERNAL_KEY", minLength: 16);
        RequireSecret("MASKFLOW_ADMIN_API_KEY", minLength: 16);
        RequireSecret("MASKFLOW_MYSQL", minLength: 8);
        RequireSecret("MASKFLOW_MINIO_SECRET_KEY", minLength: 16);
        RequireDisabled("MASKFLOW_BILLING_DEV_MODE");
        RequireDisabled("MASKFLOW_PASSWORD_RESET_INLINE");
    }

    static void RequireSecret(string name, int minLength)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} must be set in Production.");
        }

        if (value.Length < minLength)
        {
            throw new InvalidOperationException($"{name} must be at least {minLength} characters in Production.");
        }

        if (DeniedFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{name} is using an example or development value. Set a unique secret before deploying.");
        }
    }

    static void RequireDisabled(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{name} must be disabled in Production.");
        }
    }
}
