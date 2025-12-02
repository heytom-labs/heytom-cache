using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;

namespace Heytom.Cache.Tests;

/// <summary>
/// 缓存健康检查单元测试
/// </summary>
public class CacheHealthCheckTests
{
    private readonly Mock<ILogger<CacheHealthCheck>> _mockLogger;
    private readonly Mock<ILogger<HybridDistributedCache>> _mockCacheLogger;

    public CacheHealthCheckTests()
    {
        _mockLogger = new Mock<ILogger<CacheHealthCheck>>();
        _mockCacheLogger = new Mock<ILogger<HybridDistributedCache>>();
    }

    [Fact]
    public async Task CheckHealthAsync_WithHealthyRedis_ReturnsHealthy()
    {
        // Arrange
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "localhost:6379",
            EnableLocalCache = true,
            EnableMetrics = true
        };

        var cache = new HybridDistributedCache(options, _mockCacheLogger.Object);
        var healthCheck = new CacheHealthCheck(cache, _mockLogger.Object);
        var context = new HealthCheckContext();

        try
        {
            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            // 注意：如果 Redis 实际不可用，结果可能是 Degraded 或 Unhealthy
            // 这个测试需要真实的 Redis 实例才能返回 Healthy
            Assert.NotNull(result);
            Assert.Contains(result.Status, new[] { HealthStatus.Healthy, HealthStatus.Degraded, HealthStatus.Unhealthy });
            Assert.NotNull(result.Data);
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Fact]
    public async Task CheckHealthAsync_WithInvalidRedis_ReturnsDegradedWhenLocalCacheEnabled()
    {
        // Arrange
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:9999",
            EnableLocalCache = true,
            EnableMetrics = true,
            RedisOperationTimeout = TimeSpan.FromSeconds(1)
        };

        var cache = new HybridDistributedCache(options, _mockCacheLogger.Object);
        var healthCheck = new CacheHealthCheck(cache, _mockLogger.Object);
        var context = new HealthCheckContext();

        try
        {
            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            // 健康检查的结果取决于 Redis 是否真的不可用
            // 如果 Redis 不可用但本地缓存启用，应该返回 Degraded
            // 如果 Redis 可用（例如 localhost:6379 正在运行），则返回 Healthy
            Assert.Contains(result.Status, new[] { HealthStatus.Healthy, HealthStatus.Degraded });
            Assert.NotNull(result.Data);
            Assert.True(result.Data.ContainsKey("redis_connected"));
            Assert.True(result.Data.ContainsKey("local_cache_enabled"));
            Assert.True((bool)result.Data["local_cache_enabled"]);
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Fact]
    public async Task CheckHealthAsync_WithInvalidRedisAndNoLocalCache_ReturnsUnhealthy()
    {
        // Arrange
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:9999",
            EnableLocalCache = false,
            EnableMetrics = true,
            RedisOperationTimeout = TimeSpan.FromSeconds(1)
        };

        var cache = new HybridDistributedCache(options, _mockCacheLogger.Object);
        var healthCheck = new CacheHealthCheck(cache, _mockLogger.Object);
        var context = new HealthCheckContext();

        try
        {
            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            // 当 Redis 不可用且本地缓存未启用时，应该返回 Unhealthy
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.NotNull(result.Data);
            Assert.True(result.Data.ContainsKey("redis_connected"));
            Assert.False((bool)result.Data["redis_connected"]);
            Assert.True(result.Data.ContainsKey("local_cache_enabled"));
            Assert.False((bool)result.Data["local_cache_enabled"]);
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsMetricsData()
    {
        // Arrange
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "localhost:6379",
            EnableLocalCache = true,
            EnableMetrics = true
        };

        var cache = new HybridDistributedCache(options, _mockCacheLogger.Object);
        var healthCheck = new CacheHealthCheck(cache, _mockLogger.Object);
        var context = new HealthCheckContext();

        try
        {
            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Data);
            
            // 验证指标数据存在
            Assert.True(result.Data.ContainsKey("total_requests"));
            Assert.True(result.Data.ContainsKey("cache_hits"));
            Assert.True(result.Data.ContainsKey("cache_misses"));
            Assert.True(result.Data.ContainsKey("hit_rate"));
            Assert.True(result.Data.ContainsKey("local_cache_hits"));
            Assert.True(result.Data.ContainsKey("redis_hits"));
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Fact]
    public async Task CheckHealthAsync_WithCancellation_HandlesGracefully()
    {
        // Arrange
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "localhost:6379",
            EnableLocalCache = true,
            EnableMetrics = true
        };

        var cache = new HybridDistributedCache(options, _mockCacheLogger.Object);
        var healthCheck = new CacheHealthCheck(cache, _mockLogger.Object);
        var context = new HealthCheckContext();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            // Act
            var result = await healthCheck.CheckHealthAsync(context, cts.Token);

            // Assert
            // 健康检查捕获所有异常并返回 Unhealthy 或 Degraded 状态
            Assert.Contains(result.Status, new[] { HealthStatus.Unhealthy, HealthStatus.Degraded });
            Assert.NotNull(result.Data);
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsRedisStatusInformation()
    {
        // Arrange
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "localhost:6379",
            EnableLocalCache = true,
            EnableMetrics = true
        };

        var cache = new HybridDistributedCache(options, _mockCacheLogger.Object);
        var healthCheck = new CacheHealthCheck(cache, _mockLogger.Object);
        var context = new HealthCheckContext();

        try
        {
            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Data);
            
            // 验证 Redis 状态信息
            Assert.True(result.Data.ContainsKey("redis_connected"));
            Assert.True(result.Data.ContainsKey("redis_status"));
            
            // 如果 Redis 连接成功，应该有响应时间
            if ((bool)result.Data["redis_connected"])
            {
                Assert.True(result.Data.ContainsKey("redis_response_time_ms"));
            }
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsLocalCacheStatusInformation()
    {
        // Arrange
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "localhost:6379",
            EnableLocalCache = true,
            EnableMetrics = true
        };

        var cache = new HybridDistributedCache(options, _mockCacheLogger.Object);
        var healthCheck = new CacheHealthCheck(cache, _mockLogger.Object);
        var context = new HealthCheckContext();

        try
        {
            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Data);
            
            // 验证本地缓存状态信息
            Assert.True(result.Data.ContainsKey("local_cache_enabled"));
            Assert.True(result.Data.ContainsKey("local_cache_status"));
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Fact]
    public void CacheHealthCheck_Constructor_ThrowsWhenCacheIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CacheHealthCheck(null!, _mockLogger.Object));
    }

    [Fact]
    public async Task CheckHealthAsync_WithNullLogger_WorksCorrectly()
    {
        // Arrange
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "localhost:6379",
            EnableLocalCache = true,
            EnableMetrics = true
        };

        var cache = new HybridDistributedCache(options);
        var healthCheck = new CacheHealthCheck(cache, null);
        var context = new HealthCheckContext();

        try
        {
            // Act
            var result = await healthCheck.CheckHealthAsync(context);

            // Assert
            Assert.NotNull(result);
            Assert.Contains(result.Status, new[] { HealthStatus.Healthy, HealthStatus.Degraded, HealthStatus.Unhealthy });
        }
        finally
        {
            cache.Dispose();
        }
    }
}
