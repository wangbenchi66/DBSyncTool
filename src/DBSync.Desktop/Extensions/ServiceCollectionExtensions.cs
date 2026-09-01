using System.Reflection;
using DBSync.Desktop.Services;
using DBSync.Desktop.Storage;
using Microsoft.Extensions.DependencyInjection;
using WBC66.Autofac.Core;

namespace DBSync.Desktop.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRegisterDependencies(this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;
        var dependencyTypes = assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDependency).IsAssignableFrom(type))
            .ToList();

        foreach (var type in dependencyTypes)
        {
            var serviceTypes = type.GetInterfaces()
                .Where(i => i != typeof(IDependency))
                .ToList();

            if (serviceTypes.Count == 0)
                services.AddSingleton(type);
            else
                foreach (var serviceType in serviceTypes)
                    services.AddSingleton(serviceType, type);
        }

        return services;
    }
}
