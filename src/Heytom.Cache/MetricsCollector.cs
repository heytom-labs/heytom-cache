using System.Diagnostics;

namespace Heytom.Cache;

/// <summary>
/// 缓存指标收集器
/// 负责收集和计算缓存操作的性能指标
/// </summary>
public class MetricsCollector
{
    private long _totalRequests;
    private long _cacheHits;
    private long _cacheMisses;
    private long _localCacheHits;
    private long _redisHits;
    private long _totalResponseTimeMs;
    private readonly object _lock = new();

    /// <summary>
    /// 记录缓存命中
    /// </summary>
    /// <param name="isLocalCache">是否来自本地缓存</param>
    /// <param name="responseTimeMs">响应时间（毫秒）</param>
    public void RecordHit(bool isLocalCache, long responseTimeMs)
    {
        lock (_lock)
        {
            _totalRequests++;
            _cacheHits++;
            _totalResponseTimeMs += responseTimeMs;
            
            if (isLocalCache)
            {
                _localCacheHits++;
            }
            else
            {
                _redisHits++;
            }
        }
    }

    /// <summary>
    /// 记录缓存未命中
    /// </summary>
    /// <param name="responseTimeMs">响应时间（毫秒）</param>
    public void RecordMiss(long responseTimeMs)
    {
        lock (_lock)
        {
            _totalRequests++;
            _cacheMisses++;
            _totalResponseTimeMs += responseTimeMs;
        }
    }

    /// <summary>
    /// 记录操作（用于 Set/Remove 等非查询操作）
    /// </summary>
    /// <param name="responseTimeMs">响应时间（毫秒）</param>
    public void RecordOperation(long responseTimeMs)
    {
        lock (_lock)
        {
            _totalResponseTimeMs += responseTimeMs;
        }
    }

    /// <summary>
    /// 获取当前的缓存指标快照
    /// </summary>
    /// <returns>缓存指标</returns>
    public CacheMetrics GetMetrics()
    {
        lock (_lock)
        {
            return new CacheMetrics
            {
                TotalRequests = _totalRequests,
                CacheHits = _cacheHits,
                CacheMisses = _cacheMisses,
                LocalCacheHits = _localCacheHits,
                RedisHits = _redisHits,
                AverageResponseTimeMs = _totalRequests > 0 
                    ? (double)_totalResponseTimeMs / _totalRequests 
                    : 0
            };
        }
    }

    /// <summary>
    /// 重置所有指标
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _totalRequests = 0;
            _cacheHits = 0;
            _cacheMisses = 0;
            _localCacheHits = 0;
            _redisHits = 0;
            _totalResponseTimeMs = 0;
        }
    }

    /// <summary>
    /// 测量操作执行时间的辅助方法
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="operation">要执行的操作</param>
    /// <param name="onComplete">操作完成后的回调，接收执行时间和结果</param>
    /// <returns>操作的返回值</returns>
    public T MeasureOperation<T>(Func<T> operation, Action<long, T> onComplete)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = operation();
            stopwatch.Stop();
            onComplete(stopwatch.ElapsedMilliseconds, result);
            return result;
        }
        catch
        {
            stopwatch.Stop();
            throw;
        }
    }

    /// <summary>
    /// 测量异步操作执行时间的辅助方法
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="operation">要执行的异步操作</param>
    /// <param name="onComplete">操作完成后的回调，接收执行时间和结果</param>
    /// <returns>操作的返回值</returns>
    public async Task<T> MeasureOperationAsync<T>(Func<Task<T>> operation, Action<long, T> onComplete)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await operation().ConfigureAwait(false);
            stopwatch.Stop();
            onComplete(stopwatch.ElapsedMilliseconds, result);
            return result;
        }
        catch
        {
            stopwatch.Stop();
            throw;
        }
    }
}
