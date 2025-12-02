using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Heytom.Cache;

/// <summary>
/// 服务注册扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加分布式缓存服务，使用 Action 配置选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项的委托</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddDistributedCache(
        this IServiceCollection services,
        Action<HybridCacheOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        // 配置选项
        services.Configure(configureOptions);
        
        // 注册核心服务
        RegisterCoreServices(services);
        
        return services;
    }

    /// <summary>
    /// 添加分布式缓存服务，从 IConfiguration 绑定选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置节</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddDistributedCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 从配置绑定选项
        services.Configure<HybridCacheOptions>(configuration);
        
        // 注册核心服务
        RegisterCoreServices(services);
        
        return services;
    }

    /// <summary>
    /// 添加分布式缓存服务，使用默认配置
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="redisConnectionString">Redis 连接字符串</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddDistributedCache(
        this IServiceCollection services,
        string redisConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new ArgumentException("Redis connection string cannot be null or empty.", nameof(redisConnectionString));
        }

        // 配置选项
        services.Configure<HybridCacheOptions>(options =>
        {
            options.RedisConnectionString = redisConnectionString;
        });
        
        // 注册核心服务
        RegisterCoreServices(services);
        
        return services;
    }

    /// <summary>
    /// 添加缓存健康检查
    /// </summary>
    /// <param name="builder">健康检查构建器</param>
    /// <param name="name">健康检查名称（可选，默认为 "cache"）</param>
    /// <param name="failureStatus">失败状态（可选，默认为 Degraded）</param>
    /// <param name="tags">标签（可选）</param>
    /// <returns>健康检查构建器</returns>
    public static IHealthChecksBuilder AddCacheHealthCheck(
        this IHealthChecksBuilder builder,
        string? name = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddCheck<CacheHealthCheck>(
            name ?? "cache",
            failureStatus ?? HealthStatus.Degraded,
            tags ?? []);
    }

    /// <summary>
    /// 注册核心服务
    /// </summary>
    private static void RegisterCoreServices(IServiceCollection services)
    {
        // 注册序列化器（默认使用 JsonSerializer）
        services.AddSingleton<ISerializer, JsonSerializer>();
        
        // 注册 HybridDistributedCache 作为 IDistributedCache 和 IRedisExtensions 的实现
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;
            var logger = serviceProvider.GetService<ILogger<HybridDistributedCache>>();
            
            return new HybridDistributedCache(options, logger);
        });
        
        // 注册 IDistributedCache 接口
        services.AddSingleton<IDistributedCache>(serviceProvider =>
            serviceProvider.GetRequiredService<HybridDistributedCache>());
        
        // 注册 IRedisExtensions 接口
        services.AddSingleton<IRedisExtensions>(serviceProvider =>
            serviceProvider.GetRequiredService<HybridDistributedCache>());
    }
}
