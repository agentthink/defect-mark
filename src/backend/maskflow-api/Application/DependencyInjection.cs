using Microsoft.Extensions.DependencyInjection;

namespace MaskFlow.Api.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMaskFlowApplication(this IServiceCollection services)
    {
        services.AddScoped<ProjectService>();
        return services;
    }
}
