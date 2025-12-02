using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Heytom.Cache.Tests;

/// <summary>
/// 服务注册扩展方法的单元测试
/// 验证依赖注入配置的正确性
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDistributedCache_WithAction_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "localhost:6379";

        // Act
        services.AddDistributedCache(options =>
        {
            options.RedisConnectionString = connectionString;
            options.EnableLocalCache = true;
            options.LocalCacheMaxSize = 500;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - 验证 IDistributedCache 已注册
        var distributedCache = serviceProvider.GetService<IDistributedCache>();
        Assert.NotNull(distributedCache);
        Assert.IsType<HybridDistributedCache>(distributedCache);

        // Assert - 验证 IRedisExtensions 已注册
        var redisExtensions = serviceProvider.GetService<IRedisExtensions>();
        Assert.NotNull(redisExtensions);
        Assert.IsType<HybridDistributedCache>(redisExtensions);

        // Assert - 验证 ISerializer 已注册
        var serializer = serviceProvider.GetService<ISerializer>();
        Assert.NotNull(serializer);
        Assert.IsType<JsonSerializer>(serializer);

        // Assert - 验证配置选项已正确绑定
        var options = serviceProvider.GetService<IOptions<HybridCacheOptions>>();
        Assert.NotNull(options);
        Assert.Equal(connectionString, options.Value.RedisConnectionString);
        Assert.True(options.Value.EnableLocalCache);
        Assert.Equal(500, options.Value.LocalCacheMaxSize);
    }

    [Fact]
    public void AddDistributedCache_WithConnectionString_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "localhost:6379";

        // Act
        services.AddDistributedCache(connectionString);

        var serviceProvider = services.BuildServiceProvider();

        // Assert - 验证 IDistributedCache 已注册
        var distributedCache = serviceProvider.GetService<IDistributedCache>();
        Assert.NotNull(distributedCache);
        Assert.IsType<HybridDistributedCache>(distributedCache);

        // Assert - 验证配置选项已正确绑定
        var options = serviceProvider.GetService<IOptions<HybridCacheOptions>>();
        Assert.NotNull(options);
        Assert.Equal(connectionString, options.Value.RedisConnectionString);
    }

    [Fact]
    public void AddDistributedCache_WithConfiguration_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configurationData = new Dictionary<string, string?>
        {
            { "RedisConnectionString", "localhost:6379" },
            { "EnableLocalCache", "true" },
            { "LocalCacheMaxSize", "2000" },
            { "LocalCacheDefaultExpiration", "00:10:00" },
            { "RedisOperationTimeout", "00:00:10" },
            { "EnableMetrics", "false" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        // Act
        services.AddDistributedCache(configuration);

        var serviceProvider = services.BuildServiceProvider();

        // Assert - 验证 IDistributedCache 已注册
        var distributedCache = serviceProvider.GetService<IDistributedCache>();
        Assert.NotNull(distributedCache);
        Assert.IsType<HybridDistributedCache>(distributedCache);

        // Assert - 验证配置选项已正确绑定
        var options = serviceProvider.GetService<IOptions<HybridCacheOptions>>();
        Assert.NotNull(options);
        Assert.Equal("localhost:6379", options.Value.RedisConnectionString);
        Assert.True(options.Value.EnableLocalCache);
        Assert.Equal(2000, options.Value.LocalCacheMaxSize);
        Assert.Equal(TimeSpan.FromMinutes(10), options.Value.LocalCacheDefaultExpiration);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Value.RedisOperationTimeout);
        Assert.False(options.Value.EnableMetrics);
    }

    [Fact]
    public void AddDistributedCache_ReturnsSameInstanceForBothInterfaces()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDistributedCache("localhost:6379");

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var distributedCache = serviceProvider.GetService<IDistributedCache>();
        var redisExtensions = serviceProvider.GetService<IRedisExtensions>();

        // Assert - 验证两个接口返回同一个实例（单例）
        Assert.NotNull(distributedCache);
        Assert.NotNull(redisExtensions);
        Assert.Same(distributedCache, redisExtensions);
    }

    [Fact]
    public void AddDistributedCache_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            services!.AddDistributedCache("localhost:6379"));
    }

    [Fact]
    public void AddDistributedCache_WithNullAction_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<HybridCacheOptions>? configureOptions = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            services.AddDistributedCache(configureOptions!));
    }

    [Fact]
    public void AddDistributedCache_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        IConfiguration? configuration = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            services.AddDistributedCache(configuration!));
    }

    [Fact]
    public void AddDistributedCache_WithEmptyConnectionString_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            services.AddDistributedCache(string.Empty));
    }

    [Fact]
    public void AddDistributedCache_RegistersHybridDistributedCacheAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDistributedCache("localhost:6379");

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var instance1 = serviceProvider.GetService<IDistributedCache>();
        var instance2 = serviceProvider.GetService<IDistributedCache>();

        // Assert - 验证返回同一个实例（单例模式）
        Assert.NotNull(instance1);
        Assert.NotNull(instance2);
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void AddDistributedCache_WithDefaultOptions_UsesDefaultValues()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDistributedCache("localhost:6379");

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetService<IOptions<HybridCacheOptions>>();

        // Assert - 验证默认值
        Assert.NotNull(options);
        Assert.Equal("localhost:6379", options.Value.RedisConnectionString);
        Assert.True(options.Value.EnableLocalCache); // 默认启用
        Assert.Equal(1000, options.Value.LocalCacheMaxSize); // 默认 1000
        Assert.Equal(TimeSpan.FromMinutes(5), options.Value.LocalCacheDefaultExpiration); // 默认 5 分钟
        Assert.Equal(TimeSpan.FromSeconds(5), options.Value.RedisOperationTimeout); // 默认 5 秒
        Assert.True(options.Value.EnableMetrics); // 默认启用
    }
}
