using System.Collections.Concurrent;
using System.Linq.Expressions;
using Friday.Abstractions;
using Infrastructure.FridayMediator.Behaviors;

namespace Infrastructure.FridayMediator.Core;

//Mediator is a design pattern that allows for decoupling of components in a system by using a central hub to manage communication between them. Mediator's name is derived from the Latin word "mediator," which means "one who mediates or intervenes." In software design, the Mediator pattern is used to reduce dependencies between components, making it easier to maintain and extend the system. The pattern promotes loose coupling by allowing components to communicate through a mediator object rather than directly with each other. This can lead to a more organized and manageable codebase, especially in complex systems with many interacting components. It is named after Friday inspired by Tony Stark's AI assistant after Jarvis.
public interface IFriday
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    Task Publish(INotification notification, CancellationToken cancellationToken = default, bool fireAndForget = true);
}

public class Friday : IFriday
{
    private readonly ILogger<Friday> _logger;
    private readonly IServiceProvider _serviceProvider;
    private static readonly ConcurrentDictionary<Type, Delegate> _handlerCache = new();
    private static readonly ConcurrentDictionary<Type, List<Func<object, INotification, CancellationToken, Task>>> _notificationHandlerCache = new();

    public Friday(IServiceProvider serviceProvider, ILogger<Friday> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Send a request to the appropriate handler and return the response.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="TResponse"></typeparam>
    /// <returns></returns>
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        //Determine Handler Type
        // The method dynamically creates the IRequestHandler<TRequest, TResponse> interface using the runtime type of the request:
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResponse));

        // Resolve the Handler
        // It retrieves the handler instance from the DI container:
        var handler = _serviceProvider.GetRequiredService(handlerType);

        var method = handlerType.GetMethod("Handle")!;
        
        // Use Compiled Delegate (Caching)
        // Instead of using reflection every time, the method uses a ConcurrentDictionary to cache a compiled delegate that can invoke the Handle method:
        var invoker = (Func<object, object, CancellationToken, Task<TResponse>>)_handlerCache.GetOrAdd(handlerType, _ =>
        {
            var handlerParam = Expression.Parameter(typeof(object), "handler");
            var requestParam = Expression.Parameter(typeof(object), "request");
            var tokenParam = Expression.Parameter(typeof(CancellationToken), "ct");

            var castHandler = Expression.Convert(handlerParam, handlerType);
            var castRequest = Expression.Convert(requestParam, request.GetType());

            var call = Expression.Call(castHandler, method, castRequest, tokenParam);
            var lambda = Expression.Lambda<Func<object, object, CancellationToken, Task<TResponse>>>(call, handlerParam, requestParam, tokenParam);
            return lambda.Compile();
        });

        // Run Through Behaviors
        // Finally, the handler call is wrapped with the ApplyBehaviors method to run any registered middleware-like components:
        return await ApplyBehaviors(request, () => invoker(handler, request, cancellationToken), cancellationToken);
    }

    public async Task Publish(INotification notification, CancellationToken cancellationToken = default, bool fireAndForget = true)
    {
        if (notification == null) throw new ArgumentNullException(nameof(notification));

        var notificationType = notification.GetType();
        var handlerType = typeof(INotificationHandler<>).MakeGenericType(notificationType);

        using var scope = _serviceProvider.CreateScope();
        var scopedProvider = scope.ServiceProvider;
        var handlers = scopedProvider.GetServices(handlerType).ToList();

        if (!handlers.Any())
        {
            _logger.LogInformation("No handlers found for notification type {NotificationType}", notificationType.Name);
            return;
        }

        var delegates = _notificationHandlerCache.GetOrAdd(notificationType, _ =>
        {
            return handlers.Select(handler =>
            {
                var handlerTypeImpl = handler?.GetType();

                var handlerParam = Expression.Parameter(typeof(object), "handler");
                var notificationParam = Expression.Parameter(typeof(INotification), "notification");
                var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

                var castedHandler = Expression.Convert(handlerParam, handlerTypeImpl!);
                var castedNotification = Expression.Convert(notificationParam, notificationType);

                var method = handlerTypeImpl?.GetMethod("Handle")!;
                var call = Expression.Call(castedHandler, method, castedNotification, ctParam);

                return Expression.Lambda<Func<object, INotification, CancellationToken, Task>>(call, handlerParam, notificationParam, ctParam).Compile();
            }).ToList();
        });

        var tasks = new List<Task>();

        for (int i = 0; i < handlers.Count; i++)
        {
            var handlerInstance = handlers[i];
            var invokeDelegate = delegates[i];

            Task handlerTask = Task.Run(async () =>
            {
                try
                {
                    _logger.LogDebug("Invoking handler {HandlerType} for notification {NotificationType}", handlerInstance?.GetType().Name,
                        notificationType.Name);
                    await invokeDelegate(handlerInstance!, notification, cancellationToken);
                    _logger.LogInformation("Handler {HandlerType} completed successfully.", handlerInstance?.GetType().Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in handler {HandlerType} for notification {NotificationType}", handlerInstance?.GetType().Name,
                        notificationType.Name);
                }
            });

            if (!fireAndForget)
                tasks.Add(handlerTask);
        }

        if (!fireAndForget)
        {
            await Task.WhenAll(tasks);
        }
        else
        {
            // Optional: background fire-and-forget runner
            _ = Task.WhenAll(tasks); // Don't await, but track unhandled exceptions globally if needed
        }
    }

    private async Task<TResponse> ApplyBehaviors<TResponse>(
        IRequest<TResponse> request,
        Func<Task<TResponse>> execute,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType();
        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var behaviors = _serviceProvider.GetServices(behaviorType).ToList();
        if (!behaviors.Any())
        {
            return await execute();
        }

        var enumerator = behaviors.GetEnumerator();

        Task<TResponse> InvokeNext()
        {
            if (!enumerator.MoveNext()) return execute();

            var current = enumerator.Current;
            var method = current?.GetType().GetMethod("Handle")!;

            return (Task<TResponse>)method.Invoke(current, new object[] { request, (Func<Task<TResponse>>)InvokeNext, cancellationToken })!;
        }

        return await InvokeNext();
    }
}