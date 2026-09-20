using MaskFlow.Api.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace MaskFlow.Api.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMaskFlowInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IMaskFlowRepository, MySqlMaskFlowRepository>();
        services.AddSingleton<MaskFlowStore>();
        services.AddSingleton<LoginRateLimiter>();
        services.AddSingleton<SamInferenceGate>();
        return services;
    }
}
