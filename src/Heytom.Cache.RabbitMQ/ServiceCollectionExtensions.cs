using Heytom.Cache.Invalidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Heytom.Cache.RabbitMQ;

/// <summary>
/// RabbitMQ 缓存失效通知服务注册扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加基于 RabbitMQ 的缓存失效通知服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项的委托</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddRabbitMQCacheInvalidation(
        this IServiceCollection services,
        Action<RabbitMQCacheInvalidationOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        // 配置选项
        services.Configure(configureOptions);

        // 注册服务
        RegisterRabbitMQServices(services);

        return services;
    }

    /// <summary>
    /// 添加基于 RabbitMQ 的缓存失效通知服务，从 IConfiguration 绑定选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置节</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddRabbitMQCacheInvalidation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 从配置绑定选项
        services.Configure<RabbitMQCacheInvalidationOptions>(configuration);

        // 注册服务
        RegisterRabbitMQServices(services);

        return services;
    }

    /// <summary>
    /// 添加基于 RabbitMQ 的缓存失效通知服务，使用连接字符串
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="connectionString">RabbitMQ 连接字符串</param>
    /// <param name="exchangeName">Exchange 名称（可选）</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddRabbitMQCacheInvalidation(
        this IServiceCollection services,
        string connectionString,
        string? exchangeName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("RabbitMQ connection string cannot be null or empty.", nameof(connectionString));
        }

        // 配置选项
        services.Configure<RabbitMQCacheInvalidationOptions>(options =>
        {
            options.ConnectionString = connectionString;
            if (!string.IsNullOrWhiteSpace(exchangeName))
            {
                options.ExchangeName = exchangeName;
            }
        });

        // 注册服务
        RegisterRabbitMQServices(services);

        return services;
    }

    /// <summary>
    /// 注册 RabbitMQ 服务
    /// </summary>
    private static void RegisterRabbitMQServices(IServiceCollection services)
    {
        // 注册缓存失效通知器（RabbitMQ 实现）
        services.AddSingleton<ICacheInvalidationNotifier>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<RabbitMQCacheInvalidationOptions>>().Value;
            var logger = serviceProvider.GetService<ILogger<RabbitMQCacheInvalidationNotifier>>();

            return new RabbitMQCacheInvalidationNotifier(options, logger);
        });

        // 注册缓存失效订阅器（RabbitMQ 实现）
        services.AddSingleton<ICacheInvalidationSubscriber>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<RabbitMQCacheInvalidationOptions>>().Value;
            var logger = serviceProvider.GetService<ILogger<RabbitMQCacheInvalidationSubscriber>>();

            return new RabbitMQCacheInvalidationSubscriber(options, logger);
        });
    }
}
