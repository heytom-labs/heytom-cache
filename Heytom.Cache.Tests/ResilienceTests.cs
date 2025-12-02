using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Heytom.Cache.Tests;

/// <summary>
/// 单元测试：Redis 连接失败处理和弹性策略
/// 测试要求 7.1, 7.2, 7.3
/// </summary>
public class ResilienceTests
{
    [Fact]
    public async Task GetAsync_WithRedisFailureAndLocalCache_ShouldDegradeToLocalCache()
    {
        // Arrange - 使用无效的 Redis 连接字符串来模拟连接失败
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = true,
            LocalCacheMaxSize = 100,
            LocalCacheDefaultExpiration = TimeSpan.FromMinutes(5),
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");

        // Act - 尝试获取不存在的键（Redis 会失败，但本地缓存应该返回 null）
        var result = await cache.GetAsync(key);

        // Assert - 应该降级到本地缓存并返回 null（因为本地缓存中也不存在）
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_WithRedisFailureAndLocalCache_ShouldDegradeToLocalCacheOnly()
    {
        // Arrange - 使用无效的 Redis 连接字符串
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = true,
            LocalCacheMaxSize = 100,
            LocalCacheDefaultExpiration = TimeSpan.FromMinutes(5),
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        // Act - 设置值（Redis 会失败，但应该降级到仅本地缓存）
        await cache.SetAsync(key, value, cacheOptions);

        // 从本地缓存获取
        var result = await cache.GetAsync(key);

        // Assert - 应该能从本地缓存获取到值
        Assert.NotNull(result);
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task GetAsync_WithRedisFailureAndNoLocalCache_ShouldThrowInvalidOperationException()
    {
        // Arrange - 使用无效的 Redis 连接字符串且禁用本地缓存
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = false, // 禁用本地缓存
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";

        // Act & Assert - 应该抛出 InvalidOperationException
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await cache.GetAsync(key));

        // 验证异常消息
        Assert.Contains("Redis is unavailable", exception.Message);
        Assert.Contains("local cache is disabled", exception.Message);
    }

    [Fact]
    public async Task SetAsync_WithRedisFailureAndNoLocalCache_ShouldThrowInvalidOperationException()
    {
        // Arrange - 使用无效的 Redis 连接字符串且禁用本地缓存
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = false, // 禁用本地缓存
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        // Act & Assert - 应该抛出 InvalidOperationException
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await cache.SetAsync(key, value, cacheOptions));

        // 验证异常消息
        Assert.Contains("Redis is unavailable", exception.Message);
        Assert.Contains("local cache is disabled", exception.Message);
    }

    [Fact]
    public async Task RemoveAsync_WithRedisFailureAndLocalCache_ShouldDegradeToLocalCacheOnly()
    {
        // Arrange - 使用无效的 Redis 连接字符串
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = true,
            LocalCacheMaxSize = 100,
            LocalCacheDefaultExpiration = TimeSpan.FromMinutes(5),
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        // 先设置值到本地缓存
        await cache.SetAsync(key, value, cacheOptions);

        // Act - 删除值（Redis 会失败，但应该从本地缓存删除）
        await cache.RemoveAsync(key);

        // 验证已从本地缓存删除
        var result = await cache.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Get_WithRedisFailureAndLocalCache_ShouldDegradeToLocalCache()
    {
        // Arrange - 使用无效的 Redis 连接字符串
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = true,
            LocalCacheMaxSize = 100,
            LocalCacheDefaultExpiration = TimeSpan.FromMinutes(5),
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";

        // Act - 同步获取（Redis 会失败，但本地缓存应该返回 null）
        var result = cache.Get(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Set_WithRedisFailureAndLocalCache_ShouldDegradeToLocalCacheOnly()
    {
        // Arrange - 使用无效的 Redis 连接字符串
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = true,
            LocalCacheMaxSize = 100,
            LocalCacheDefaultExpiration = TimeSpan.FromMinutes(5),
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        // Act - 同步设置值（Redis 会失败，但应该降级到仅本地缓存）
        cache.Set(key, value, cacheOptions);

        // 从本地缓存获取
        var result = cache.Get(key);

        // Assert - 应该能从本地缓存获取到值
        Assert.NotNull(result);
        Assert.Equal(value, result);
    }

    [Fact]
    public void Get_WithRedisFailureAndNoLocalCache_ShouldThrowInvalidOperationException()
    {
        // Arrange - 使用无效的 Redis 连接字符串且禁用本地缓存
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = false, // 禁用本地缓存
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";

        // Act & Assert - 应该抛出 InvalidOperationException
        var exception = Assert.Throws<InvalidOperationException>(() => cache.Get(key));

        // 验证异常消息
        Assert.Contains("Redis is unavailable", exception.Message);
        Assert.Contains("local cache is disabled", exception.Message);
    }

    [Fact]
    public void Remove_WithRedisFailureAndLocalCache_ShouldDegradeToLocalCacheOnly()
    {
        // Arrange - 使用无效的 Redis 连接字符串
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = true,
            LocalCacheMaxSize = 100,
            LocalCacheDefaultExpiration = TimeSpan.FromMinutes(5),
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        // 先设置值到本地缓存
        cache.Set(key, value, cacheOptions);

        // Act - 同步删除值（Redis 会失败，但应该从本地缓存删除）
        cache.Remove(key);

        // 验证已从本地缓存删除
        var result = cache.Get(key);

        // Assert
        Assert.Null(result);
    }
}
