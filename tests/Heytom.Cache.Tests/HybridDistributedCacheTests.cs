using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace Heytom.Cache.Tests;

/// <summary>
/// 单元测试：HybridDistributedCache 基本功能
/// </summary>
public class HybridDistributedCacheTests : IDisposable
{
    private readonly HybridDistributedCache _cache;
    private readonly HybridCacheOptions _options;

    public HybridDistributedCacheTests()
    {
        // 配置测试选项 - 使用本地 Redis
        _options = new HybridCacheOptions
        {
            RedisConnectionString = "localhost:6379",
            EnableLocalCache = true,
            LocalCacheMaxSize = 100,
            LocalCacheDefaultExpiration = TimeSpan.FromMinutes(5),
            RedisOperationTimeout = TimeSpan.FromSeconds(5)
        };

        _cache = new HybridDistributedCache(_options);
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ShouldReturnSameValue()
    {
        // Arrange
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        try
        {
            // Act
            await _cache.SetAsync(key, value, options);
            var result = await _cache.GetAsync(key);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(value, result);
        }
        finally
        {
            // Cleanup
            await _cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task GetAsync_NonExistentKey_ShouldReturnNull()
    {
        // Arrange
        var key = $"non-existent-key-{Guid.NewGuid()}";

        // Act
        var result = await _cache.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_ShouldDeleteEntry()
    {
        // Arrange
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        try
        {
            // Act
            await _cache.SetAsync(key, value, options);
            await _cache.RemoveAsync(key);
            var result = await _cache.GetAsync(key);

            // Assert
            Assert.Null(result);
        }
        finally
        {
            // Cleanup (in case test fails)
            await _cache.RemoveAsync(key);
        }
    }

    [Fact]
    public void Set_ThenGet_ShouldReturnSameValue()
    {
        // Arrange
        var key = $"test-key-sync-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value-sync");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        try
        {
            // Act
            _cache.Set(key, value, options);
            var result = _cache.Get(key);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(value, result);
        }
        finally
        {
            // Cleanup
            _cache.Remove(key);
        }
    }

    [Fact]
    public async Task SetAsync_WithLocalCache_ShouldPopulateLocalCache()
    {
        // Arrange
        var key = $"test-key-local-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value-local");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        try
        {
            // Act
            await _cache.SetAsync(key, value, options);
            
            // First get should hit local cache
            var result1 = await _cache.GetAsync(key);
            
            // Second get should also hit local cache
            var result2 = await _cache.GetAsync(key);

            // Assert
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            Assert.Equal(value, result1);
            Assert.Equal(value, result2);
        }
        finally
        {
            // Cleanup
            await _cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task RemoveAsync_ShouldRemoveFromBothCaches()
    {
        // Arrange
        var key = $"test-key-remove-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value-remove");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        try
        {
            // Act
            await _cache.SetAsync(key, value, options);
            
            // Verify it's in cache
            var beforeRemove = await _cache.GetAsync(key);
            Assert.NotNull(beforeRemove);
            
            // Remove from both caches
            await _cache.RemoveAsync(key);
            
            // Verify it's removed from both
            var afterRemove = await _cache.GetAsync(key);

            // Assert
            Assert.Null(afterRemove);
        }
        finally
        {
            // Cleanup
            await _cache.RemoveAsync(key);
        }
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new HybridDistributedCache(null!));
    }

    [Fact]
    public async Task GetAsync_WithEmptyKey_ShouldThrowArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _cache.GetAsync(""));
    }

    [Fact]
    public async Task SetAsync_WithNullValue_ShouldThrowArgumentNullException()
    {
        // Arrange
        var key = "test-key";
        var options = new DistributedCacheEntryOptions();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _cache.SetAsync(key, null!, options));
    }

    [Fact]
    public async Task SetAsync_WithAbsoluteExpiration_ShouldExpireAtSpecifiedTime()
    {
        // Arrange
        var key = $"test-key-abs-exp-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddSeconds(2)
        };

        try
        {
            // Act
            await _cache.SetAsync(key, value, options);
            
            // Should exist immediately
            var resultBefore = await _cache.GetAsync(key);
            Assert.NotNull(resultBefore);
            
            // Wait for expiration
            await Task.Delay(TimeSpan.FromSeconds(3));
            
            // Should be expired
            var resultAfter = await _cache.GetAsync(key);

            // Assert
            Assert.Null(resultAfter);
        }
        finally
        {
            // Cleanup
            await _cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task SetAsync_WithAbsoluteExpirationRelativeToNow_ShouldExpireAfterDuration()
    {
        // Arrange
        var key = $"test-key-rel-exp-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2)
        };

        try
        {
            // Act
            await _cache.SetAsync(key, value, options);
            
            // Should exist immediately
            var resultBefore = await _cache.GetAsync(key);
            Assert.NotNull(resultBefore);
            
            // Wait for expiration
            await Task.Delay(TimeSpan.FromSeconds(3));
            
            // Should be expired
            var resultAfter = await _cache.GetAsync(key);

            // Assert
            Assert.Null(resultAfter);
        }
        finally
        {
            // Cleanup
            await _cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task SetAsync_WithSlidingExpiration_ShouldExtendLifetimeOnAccess()
    {
        // Arrange
        var key = $"test-key-sliding-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromSeconds(3)
        };

        try
        {
            // Act
            await _cache.SetAsync(key, value, options);
            
            // Wait 2 seconds (less than expiration)
            await Task.Delay(TimeSpan.FromSeconds(2));
            
            // Access to reset sliding expiration
            var result1 = await _cache.GetAsync(key);
            Assert.NotNull(result1);
            
            // Wait another 2 seconds (would have expired without refresh)
            await Task.Delay(TimeSpan.FromSeconds(2));
            
            // Should still exist because we accessed it
            var result2 = await _cache.GetAsync(key);

            // Assert
            Assert.NotNull(result2);
        }
        finally
        {
            // Cleanup
            await _cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task RefreshAsync_WithSlidingExpiration_ShouldResetExpiration()
    {
        // Arrange
        var key = $"test-key-refresh-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromSeconds(3)
        };

        try
        {
            // Act
            await _cache.SetAsync(key, value, options);
            
            // Wait 2 seconds (less than expiration)
            await Task.Delay(TimeSpan.FromSeconds(2));
            
            // Refresh to reset sliding expiration
            await _cache.RefreshAsync(key);
            
            // Wait another 2 seconds (would have expired without refresh)
            await Task.Delay(TimeSpan.FromSeconds(2));
            
            // Should still exist because we refreshed it
            var result = await _cache.GetAsync(key);

            // Assert
            Assert.NotNull(result);
        }
        finally
        {
            // Cleanup
            await _cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task SetAsync_WithBothAbsoluteAndSlidingExpiration_ShouldExpireAtEarlierTime()
    {
        // Arrange
        var key = $"test-key-combined-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        
        // Absolute expiration is earlier (3 seconds)
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(3),
            SlidingExpiration = TimeSpan.FromSeconds(10) // Longer than absolute
        };

        try
        {
            // Act
            await _cache.SetAsync(key, value, options);
            
            // Should exist immediately
            var resultBefore = await _cache.GetAsync(key);
            Assert.NotNull(resultBefore);
            
            // Wait for absolute expiration (4 seconds to be safe)
            await Task.Delay(TimeSpan.FromSeconds(4));
            
            // Should be expired (absolute expiration wins)
            var resultAfter = await _cache.GetAsync(key);

            // Assert
            Assert.Null(resultAfter);
        }
        finally
        {
            // Cleanup
            await _cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task SetAsync_WithBothExpirations_SlidingEarlier_ShouldExpireAtSlidingTime()
    {
        // Arrange
        var key = $"test-key-combined2-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        
        // Sliding expiration is earlier (3 seconds)
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10), // Longer than sliding
            SlidingExpiration = TimeSpan.FromSeconds(3)
        };

        try
        {
            // Act
            await _cache.SetAsync(key, value, options);
            
            // Should exist immediately
            var resultBefore = await _cache.GetAsync(key);
            Assert.NotNull(resultBefore);
            
            // Wait for sliding expiration without accessing (4 seconds to be safe)
            await Task.Delay(TimeSpan.FromSeconds(4));
            
            // Should be expired (sliding expiration wins)
            var resultAfter = await _cache.GetAsync(key);

            // Assert
            Assert.Null(resultAfter);
        }
        finally
        {
            // Cleanup
            await _cache.RemoveAsync(key);
        }
    }

    public void Dispose()
    {
        _cache?.Dispose();
    }
}
