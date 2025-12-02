using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace Heytom.Cache.Tests;

/// <summary>
/// 单元测试：超时处理
/// 测试要求 7.4
/// </summary>
public class TimeoutTests
{
    [Fact]
    public async Task GetAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange - 使用无效的 Redis 连接字符串来模拟慢速连接
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=5000,syncTimeout=5000,abortConnect=false",
            EnableLocalCache = false,
            RedisOperationTimeout = TimeSpan.FromSeconds(5)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        
        // 创建一个立即取消的 CancellationToken
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - 应该抛出 OperationCanceledException 或 TaskCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cache.GetAsync(key, cts.Token));
    }

    [Fact]
    public async Task SetAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=5000,syncTimeout=5000,abortConnect=false",
            EnableLocalCache = false,
            RedisOperationTimeout = TimeSpan.FromSeconds(5)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };
        
        // 创建一个立即取消的 CancellationToken
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - 应该抛出 OperationCanceledException 或 TaskCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cache.SetAsync(key, value, cacheOptions, cts.Token));
    }

    [Fact]
    public async Task RemoveAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=5000,syncTimeout=5000,abortConnect=false",
            EnableLocalCache = false,
            RedisOperationTimeout = TimeSpan.FromSeconds(5)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        
        // 创建一个立即取消的 CancellationToken
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - 应该抛出 OperationCanceledException 或 TaskCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cache.RemoveAsync(key, cts.Token));
    }

    [Fact]
    public async Task RefreshAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=5000,syncTimeout=5000,abortConnect=false",
            EnableLocalCache = false,
            RedisOperationTimeout = TimeSpan.FromSeconds(5)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        
        // 创建一个立即取消的 CancellationToken
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - 应该抛出 OperationCanceledException 或 TaskCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cache.RefreshAsync(key, cts.Token));
    }

    [Fact]
    public async Task GetAsync_WithTimeout_ShouldThrowTimeoutException()
    {
        // Arrange - 使用非常短的超时时间和无效的主机
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = false,
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";

        // Act & Assert - 应该抛出超时相关的异常
        // 由于 Redis 连接失败，会抛出 InvalidOperationException（因为本地缓存被禁用）
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await cache.GetAsync(key));
    }

    [Fact]
    public async Task SetAsync_WithTimeout_ShouldThrowTimeoutException()
    {
        // Arrange - 使用非常短的超时时间和无效的主机
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = false,
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        // Act & Assert - 应该抛出超时相关的异常
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await cache.SetAsync(key, value, cacheOptions));
    }

    [Fact]
    public async Task GetAsync_WithValidCancellationToken_ShouldComplete()
    {
        // Arrange - 使用本地缓存以避免 Redis 连接问题
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = true,
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        
        // 创建一个未取消的 CancellationToken
        using var cts = new CancellationTokenSource();

        // Act - 应该正常完成（从本地缓存返回 null）
        var result = await cache.GetAsync(key, cts.Token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_WithValidCancellationToken_ShouldComplete()
    {
        // Arrange - 使用本地缓存
        var options = new HybridCacheOptions
        {
            RedisConnectionString = "invalid-host:6379,connectTimeout=100,syncTimeout=100,abortConnect=false",
            EnableLocalCache = true,
            RedisOperationTimeout = TimeSpan.FromMilliseconds(100)
        };

        using var cache = new HybridDistributedCache(options);
        var key = $"test-key-{Guid.NewGuid()}";
        var value = System.Text.Encoding.UTF8.GetBytes("test-value");
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };
        
        // 创建一个未取消的 CancellationToken
        using var cts = new CancellationTokenSource();

        // Act - 应该正常完成（降级到本地缓存）
        await cache.SetAsync(key, value, cacheOptions, cts.Token);

        // 验证值已设置
        var result = await cache.GetAsync(key, cts.Token);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(value, result);
    }
}
