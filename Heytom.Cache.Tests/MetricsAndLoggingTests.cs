using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Heytom.Cache.Tests;

/// <summary>
/// 单元测试：验证指标收集和日志记录功能
/// </summary>
public class MetricsAndLoggingTests : IDisposable
{
    private readonly HybridCacheOptions _options;
    private readonly Mock<ILogger<HybridDistributedCache>> _mockLogger;

    public MetricsAndLoggingTests()
    {
        _options = new HybridCacheOptions
        {
            RedisConnectionString = "localhost:6379",
            EnableLocalCache = true,
            EnableMetrics = true,
            LocalCacheMaxSize = 100,
            RedisOperationTimeout = TimeSpan.FromSeconds(5)
        };

        _mockLogger = new Mock<ILogger<HybridDistributedCache>>();
    }

    [Fact]
    public async Task GetAsync_ShouldRecordCacheHit_WhenValueExists()
    {
        // Arrange
        using var cache = new HybridDistributedCache(_options, _mockLogger.Object);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        try
        {
            // Act
            await cache.SetAsync(key, value, options);
            await cache.GetAsync(key);
            
            var metrics = cache.GetMetrics();

            // Assert
            Assert.True(metrics.TotalRequests > 0);
            Assert.True(metrics.CacheHits > 0);
            Assert.True(metrics.HitRate > 0);
        }
        finally
        {
            await cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task GetAsync_ShouldRecordCacheMiss_WhenValueDoesNotExist()
    {
        // Arrange
        using var cache = new HybridDistributedCache(_options, _mockLogger.Object);
        var key = $"non-existent-{Guid.NewGuid()}";

        // Act
        await cache.GetAsync(key);
        var metrics = cache.GetMetrics();

        // Assert
        Assert.True(metrics.TotalRequests > 0);
        Assert.True(metrics.CacheMisses > 0);
        Assert.Equal(0, metrics.HitRate);
    }

    [Fact]
    public async Task GetMetrics_ShouldCalculateCorrectHitRate()
    {
        // Arrange
        using var cache = new HybridDistributedCache(_options, _mockLogger.Object);
        var key1 = $"test-key-1-{Guid.NewGuid()}";
        var key2 = $"test-key-2-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        try
        {
            // Act
            await cache.SetAsync(key1, value, options);
            await cache.GetAsync(key1); // Hit
            await cache.GetAsync(key2); // Miss
            await cache.GetAsync(key1); // Hit
            
            var metrics = cache.GetMetrics();

            // Assert
            Assert.Equal(3, metrics.TotalRequests);
            Assert.Equal(2, metrics.CacheHits);
            Assert.Equal(1, metrics.CacheMisses);
            Assert.Equal(2.0 / 3.0, metrics.HitRate, precision: 2);
        }
        finally
        {
            await cache.RemoveAsync(key1);
        }
    }

    [Fact]
    public async Task GetMetrics_ShouldTrackLocalCacheHits()
    {
        // Arrange
        using var cache = new HybridDistributedCache(_options, _mockLogger.Object);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        try
        {
            // Act
            await cache.SetAsync(key, value, options);
            await cache.GetAsync(key); // Should hit local cache
            await cache.GetAsync(key); // Should hit local cache again
            
            var metrics = cache.GetMetrics();

            // Assert
            Assert.True(metrics.LocalCacheHits > 0);
        }
        finally
        {
            await cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task ResetMetrics_ShouldClearAllMetrics()
    {
        // Arrange
        using var cache = new HybridDistributedCache(_options, _mockLogger.Object);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        try
        {
            // Act
            await cache.SetAsync(key, value, options);
            await cache.GetAsync(key);
            
            var metricsBefore = cache.GetMetrics();
            Assert.True(metricsBefore.TotalRequests > 0);
            
            cache.ResetMetrics();
            var metricsAfter = cache.GetMetrics();

            // Assert
            Assert.Equal(0, metricsAfter.TotalRequests);
            Assert.Equal(0, metricsAfter.CacheHits);
            Assert.Equal(0, metricsAfter.CacheMisses);
            Assert.Equal(0, metricsAfter.HitRate);
        }
        finally
        {
            await cache.RemoveAsync(key);
        }
    }

    [Fact]
    public void GetMetrics_WhenMetricsDisabled_ShouldReturnEmptyMetrics()
    {
        // Arrange
        var optionsWithoutMetrics = new HybridCacheOptions
        {
            RedisConnectionString = "localhost:6379",
            EnableLocalCache = true,
            EnableMetrics = false
        };
        using var cache = new HybridDistributedCache(optionsWithoutMetrics);

        // Act
        var metrics = cache.GetMetrics();

        // Assert
        Assert.Equal(0, metrics.TotalRequests);
        Assert.Equal(0, metrics.CacheHits);
        Assert.Equal(0, metrics.CacheMisses);
    }

    [Fact]
    public async Task SetAsync_ShouldLogDebugMessage()
    {
        // Arrange
        using var cache = new HybridDistributedCache(_options, _mockLogger.Object);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        try
        {
            // Act
            await cache.SetAsync(key, value, options);

            // Assert - Verify that LogDebug was called for Redis set operation
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Set cache value in Redis")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }
        finally
        {
            await cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task GetAsync_CacheHit_ShouldLogDebugMessage()
    {
        // Arrange
        using var cache = new HybridDistributedCache(_options, _mockLogger.Object);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        try
        {
            // Act
            await cache.SetAsync(key, value, options);
            _mockLogger.Invocations.Clear(); // Clear previous logs
            await cache.GetAsync(key);

            // Assert - Verify that LogDebug was called for cache hit
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cache hit")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }
        finally
        {
            await cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task GetAsync_CacheMiss_ShouldLogDebugMessage()
    {
        // Arrange
        using var cache = new HybridDistributedCache(_options, _mockLogger.Object);
        var key = $"non-existent-{Guid.NewGuid()}";

        // Act
        await cache.GetAsync(key);

        // Assert - Verify that LogDebug was called for cache miss
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cache miss")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void Constructor_ShouldLogInitializationMessage()
    {
        // Act
        using var cache = new HybridDistributedCache(_options, _mockLogger.Object);

        // Assert - Verify that LogInformation was called during initialization
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("HybridDistributedCache initialized")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ResetMetrics_ShouldLogInformationMessage()
    {
        // Arrange
        using var cache = new HybridDistributedCache(_options, _mockLogger.Object);
        _mockLogger.Invocations.Clear(); // Clear initialization logs

        // Act
        cache.ResetMetrics();

        // Assert - Verify that LogInformation was called for metrics reset
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cache metrics reset")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_ShouldLogDebugMessage()
    {
        // Arrange
        using var cache = new HybridDistributedCache(_options, _mockLogger.Object);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        // Act
        await cache.SetAsync(key, value, options);
        _mockLogger.Invocations.Clear(); // Clear previous logs
        await cache.RemoveAsync(key);

        // Assert - Verify that LogDebug was called for remove operation
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Removed cache value")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}
