using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Heytom.Cache;

/// <summary>
/// 缓存系统健康检查实现
/// 检查 Redis 连接状态和本地缓存状态
/// </summary>
public class CacheHealthCheck : IHealthCheck
{
    private readonly HybridDistributedCache _cache;
    private readonly ILogger<CacheHealthCheck>? _logger;

    /// <summary>
    /// 初始化 CacheHealthCheck 实例
    /// </summary>
    /// <param name="cache">混合分布式缓存实例</param>
    /// <param name="logger">日志记录器（可选）</param>
    public CacheHealthCheck(
        HybridDistributedCache cache,
        ILogger<CacheHealthCheck>? logger = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger;
    }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    /// <param name="context">健康检查上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new Dictionary<string, object>();
            var isHealthy = true;
            var messages = new List<string>();

            // 检查 Redis 连接状态
            var redisStatus = await CheckRedisHealthAsync(cancellationToken);
            data["redis_connected"] = redisStatus.IsConnected;
            data["redis_status"] = redisStatus.Status;
            
            if (!redisStatus.IsConnected)
            {
                isHealthy = false;
                messages.Add($"Redis is not connected: {redisStatus.Message}");
                _logger?.LogWarning("Redis health check failed: {Message}", redisStatus.Message);
            }
            else
            {
                data["redis_response_time_ms"] = redisStatus.ResponseTimeMs;
                _logger?.LogDebug("Redis health check passed. Response time: {ResponseTime}ms", redisStatus.ResponseTimeMs);
            }

            // 检查本地缓存状态
            var localCacheStatus = CheckLocalCacheHealth();
            data["local_cache_enabled"] = localCacheStatus.IsEnabled;
            data["local_cache_status"] = localCacheStatus.Status;
            
            if (localCacheStatus.IsEnabled)
            {
                _logger?.LogDebug("Local cache is enabled and healthy");
            }

            // 获取缓存指标
            var metrics = _cache.GetMetrics();
            data["total_requests"] = metrics.TotalRequests;
            data["cache_hits"] = metrics.CacheHits;
            data["cache_misses"] = metrics.CacheMisses;
            data["hit_rate"] = metrics.HitRate;
            data["local_cache_hits"] = metrics.LocalCacheHits;
            data["redis_hits"] = metrics.RedisHits;

            // 确定整体健康状态
            if (isHealthy)
            {
                return HealthCheckResult.Healthy(
                    "Cache system is healthy",
                    data);
            }
            else if (localCacheStatus.IsEnabled)
            {
                // Redis 不可用但本地缓存可用 - 降级状态
                return HealthCheckResult.Degraded(
                    string.Join("; ", messages) + " (degraded to local cache)",
                    data: data);
            }
            else
            {
                // Redis 不可用且本地缓存未启用 - 不健康
                return HealthCheckResult.Unhealthy(
                    string.Join("; ", messages),
                    data: data);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Health check failed with exception");
            
            return HealthCheckResult.Unhealthy(
                "Health check failed with exception",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }

    /// <summary>
    /// 检查 Redis 健康状态
    /// </summary>
    private async Task<RedisHealthStatus> CheckRedisHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            
            // 尝试执行一个简单的 ping 操作
            var testKey = $"__health_check_{Guid.NewGuid()}";
            var testValue = new byte[] { 1, 2, 3 };
            
            await _cache.SetAsync(testKey, testValue, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5)
            }, cancellationToken);
            
            var retrievedValue = await _cache.GetAsync(testKey, cancellationToken);
            
            // 清理测试键
            await _cache.RemoveAsync(testKey, cancellationToken);
            
            var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            if (retrievedValue != null && retrievedValue.SequenceEqual(testValue))
            {
                return new RedisHealthStatus
                {
                    IsConnected = true,
                    Status = "Connected",
                    Message = "Redis is responding normally",
                    ResponseTimeMs = responseTime
                };
            }
            else
            {
                return new RedisHealthStatus
                {
                    IsConnected = false,
                    Status = "Error",
                    Message = "Redis returned unexpected value",
                    ResponseTimeMs = responseTime
                };
            }
        }
        catch (Exception ex)
        {
            return new RedisHealthStatus
            {
                IsConnected = false,
                Status = "Disconnected",
                Message = ex.Message,
                ResponseTimeMs = 0
            };
        }
    }

    /// <summary>
    /// 检查本地缓存健康状态
    /// </summary>
    private LocalCacheHealthStatus CheckLocalCacheHealth()
    {
        var isEnabled = _cache.IsLocalCacheEnabled();
        
        return new LocalCacheHealthStatus
        {
            IsEnabled = isEnabled,
            Status = isEnabled ? "Enabled" : "Disabled"
        };
    }

    /// <summary>
    /// Redis 健康状态
    /// </summary>
    private class RedisHealthStatus
    {
        public bool IsConnected { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public double ResponseTimeMs { get; set; }
    }

    /// <summary>
    /// 本地缓存健康状态
    /// </summary>
    private class LocalCacheHealthStatus
    {
        public bool IsEnabled { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
