namespace Heytom.Cache;

/// <summary>
/// 缓存性能指标
/// </summary>
public class CacheMetrics
{
    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; set; }
    
    /// <summary>
    /// 缓存命中数
    /// </summary>
    public long CacheHits { get; set; }
    
    /// <summary>
    /// 缓存未命中数
    /// </summary>
    public long CacheMisses { get; set; }
    
    /// <summary>
    /// 命中率
    /// </summary>
    public double HitRate => TotalRequests > 0 ? (double)CacheHits / TotalRequests : 0;
    
    /// <summary>
    /// 平均响应时间（毫秒）
    /// </summary>
    public double AverageResponseTimeMs { get; set; }
    
    /// <summary>
    /// 本地缓存命中数
    /// </summary>
    public long LocalCacheHits { get; set; }
    
    /// <summary>
    /// Redis 命中数
    /// </summary>
    public long RedisHits { get; set; }
}
