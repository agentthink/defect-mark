using MaskFlow.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

public sealed class ProductionSecretsValidatorTests
{
    sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "MaskFlow.Api.Tests";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void EnsureConfigured_SkipsInDevelopment()
    {
        var env = new TestEnvironment { EnvironmentName = Environments.Development };
        ProductionSecretsValidator.EnsureConfigured(env);
    }

    [Fact]
    public void EnsureConfigured_ThrowsWhenProductionSecretsMissing()
    {
        var env = new TestEnvironment();
        var keys = new[] { "SAM3_INTERNAL_KEY", "MASKFLOW_ADMIN_API_KEY", "MASKFLOW_MYSQL", "MASKFLOW_MINIO_SECRET_KEY" };
        var previous = keys.ToDictionary(key => key, key => Environment.GetEnvironmentVariable(key));
        try
        {
            foreach (var key in keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }

            Assert.Throws<InvalidOperationException>(() => ProductionSecretsValidator.EnsureConfigured(env));
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [Fact]
    public void EnsureConfigured_ThrowsWhenUsingDevDefaults()
    {
        var env = new TestEnvironment();
        var previous = new Dictionary<string, string?>
        {
            ["SAM3_INTERNAL_KEY"] = Environment.GetEnvironmentVariable("SAM3_INTERNAL_KEY"),
            ["MASKFLOW_ADMIN_API_KEY"] = Environment.GetEnvironmentVariable("MASKFLOW_ADMIN_API_KEY"),
            ["MASKFLOW_MYSQL"] = Environment.GetEnvironmentVariable("MASKFLOW_MYSQL"),
            ["MASKFLOW_MINIO_SECRET_KEY"] = Environment.GetEnvironmentVariable("MASKFLOW_MINIO_SECRET_KEY")
        };
        try
        {
            Environment.SetEnvironmentVariable("SAM3_INTERNAL_KEY", "maskflow-local-development-sam-key");
            Environment.SetEnvironmentVariable("MASKFLOW_ADMIN_API_KEY", "maskflow-local-development-admin-key");
            Environment.SetEnvironmentVariable("MASKFLOW_MYSQL", "Server=127.0.0.1;Database=maskflow;");
            Environment.SetEnvironmentVariable("MASKFLOW_MINIO_SECRET_KEY", "a-unique-production-minio-secret");

            var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsValidator.EnsureConfigured(env));
            Assert.Contains("development value", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [Theory]
    [InlineData("change-me-with-a-unique-random-key")]
    [InlineData("replace-me-with-a-unique-random-key")]
    public void EnsureConfigured_ThrowsForPlaceholderSecrets(string placeholder)
    {
        var env = new TestEnvironment();
        var keys = new[]
        {
            "SAM3_INTERNAL_KEY",
            "MASKFLOW_ADMIN_API_KEY",
            "MASKFLOW_MYSQL",
            "MASKFLOW_MINIO_SECRET_KEY",
            "MASKFLOW_BILLING_DEV_MODE",
            "MASKFLOW_PASSWORD_RESET_INLINE"
        };
        var previous = keys.ToDictionary(key => key, key => Environment.GetEnvironmentVariable(key));
        try
        {
            Environment.SetEnvironmentVariable("SAM3_INTERNAL_KEY", placeholder);
            Environment.SetEnvironmentVariable("MASKFLOW_ADMIN_API_KEY", "a-unique-production-admin-key");
            Environment.SetEnvironmentVariable("MASKFLOW_MYSQL", "Server=mysql;Database=maskflow;Password=a-unique-production-db-password;");
            Environment.SetEnvironmentVariable("MASKFLOW_MINIO_SECRET_KEY", "a-unique-production-minio-secret");
            Environment.SetEnvironmentVariable("MASKFLOW_BILLING_DEV_MODE", "false");
            Environment.SetEnvironmentVariable("MASKFLOW_PASSWORD_RESET_INLINE", "false");

            var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsValidator.EnsureConfigured(env));
            Assert.Contains("example or development value", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    [Theory]
    [InlineData("MASKFLOW_BILLING_DEV_MODE")]
    [InlineData("MASKFLOW_PASSWORD_RESET_INLINE")]
    public void EnsureConfigured_ThrowsWhenProductionDevFlagsEnabled(string enabledFlag)
    {
        var env = new TestEnvironment();
        var keys = new[]
        {
            "SAM3_INTERNAL_KEY",
            "MASKFLOW_ADMIN_API_KEY",
            "MASKFLOW_MYSQL",
            "MASKFLOW_MINIO_SECRET_KEY",
            "MASKFLOW_BILLING_DEV_MODE",
            "MASKFLOW_PASSWORD_RESET_INLINE"
        };
        var previous = keys.ToDictionary(key => key, key => Environment.GetEnvironmentVariable(key));
        try
        {
            Environment.SetEnvironmentVariable("SAM3_INTERNAL_KEY", "a-unique-production-sam-key");
            Environment.SetEnvironmentVariable("MASKFLOW_ADMIN_API_KEY", "a-unique-production-admin-key");
            Environment.SetEnvironmentVariable("MASKFLOW_MYSQL", "Server=mysql;Database=maskflow;Password=a-unique-production-db-password;");
            Environment.SetEnvironmentVariable("MASKFLOW_MINIO_SECRET_KEY", "a-unique-production-minio-secret");
            Environment.SetEnvironmentVariable("MASKFLOW_BILLING_DEV_MODE", "false");
            Environment.SetEnvironmentVariable("MASKFLOW_PASSWORD_RESET_INLINE", "false");
            Environment.SetEnvironmentVariable(enabledFlag, "true");

            var ex = Assert.Throws<InvalidOperationException>(() => ProductionSecretsValidator.EnsureConfigured(env));
            Assert.Contains("must be disabled", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
