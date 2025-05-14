using System.Reflection;
using FluentValidation;
using Friday.Abstractions;
using Infrastructure.FridayMediator.Behaviors;

namespace Infrastructure.FridayMediator.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFriday(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.AddScoped<IFriday, Friday>();
        services.AddHttpContextAccessor();
        // Register handlers
        var handlerTypes = assemblies.Length > 0
            ? assemblies.SelectMany(a => a.GetTypes())
            : AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes());

        foreach (var type in handlerTypes)
        {
            var handlerInterfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType &&
                            (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                             i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)));

            foreach (var handlerInterface in handlerInterfaces)
            {
                services.AddScoped(handlerInterface, type);
            }
        }

        // Register pipeline behaviors
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        
        // Optionally register validators (example: scan for validators)
        foreach (var type in handlerTypes)
        {
            if (type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                services.AddScoped(type.GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)), type);
            }
        }
        return services;
    }
}