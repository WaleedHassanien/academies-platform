using System.Data;
using Academies.BuildingBlocks.Application.Abstractions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Academies.BuildingBlocks.Infrastructure.Messaging;

internal sealed class OutboxEventPublisher(IPublishEndpoint publisher) : IEventPublisher
{
    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class => publisher.Publish(message, ct);
}

public static class MessagingExtensions
{
    /// <summary>
    /// MassTransit over RabbitMQ (<c>ConnectionStrings:RabbitMq</c>, an amqp:// URI) with the EF Core
    /// transactional outbox. Events published inside a SaveChanges go out only if the save commits.
    /// Queue names get the service name as a prefix, so two services consuming the same event
    /// each get their own queue. Without a RabbitMQ connection string it uses the in-memory
    /// transport for tests and dev.
    /// </summary>
    public static IServiceCollection AddPlatformMessaging<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
        where TContext : DbContext
    {
        var rabbit = configuration.GetConnectionString("RabbitMq");
        services.AddScoped<IEventPublisher, OutboxEventPublisher>();

        services.AddMassTransit(bus =>
        {
            bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName.ToLowerInvariant(), false));
            configureConsumers?.Invoke(bus);

            bus.AddEntityFrameworkOutbox<TContext>(outbox =>
            {
                outbox.UseMySql();
                outbox.UseBusOutbox();
                outbox.QueryDelay = TimeSpan.FromSeconds(1);

                // RepeatableRead takes gap locks on the inbox unique index, and concurrent consumers then
                // deadlock on MySQL. ReadCommitted avoids that; the unique index still guarantees once-only.
                outbox.IsolationLevel = IsolationLevel.ReadCommitted;
            });

            bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                // Retry outside the outbox, so each attempt gets a fresh scope and DbContext.
                endpoint.UseMessageRetry(r => r.Intervals(100, 500, 1000, 3000, 10000));
                endpoint.UseEntityFrameworkOutbox<TContext>(context);
            });

            if (string.IsNullOrWhiteSpace(rabbit))
            {
                bus.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
            }
            else
            {
                bus.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(new Uri(rabbit));
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        return services;
    }
}
