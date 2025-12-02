namespace Heytom.Cache;

/// <summary>
/// 混合缓存配置选项
/// </summary>
public class HybridCacheOptions
{
    /// <summary>
    /// Redis 连接字符串
    /// </summary>
    public string RedisConnectionString { get; set; } = string.Empty;
    
    /// <summary>
    /// 是否启用本地缓存
    /// </summary>
    public bool EnableLocalCache { get; set; } = true;
    
    /// <summary>
    /// 本地缓存最大条目数
    /// </summary>
    public int LocalCacheMaxSize { get; set; } = 1000;
    
    /// <summary>
    /// 本地缓存默认过期时间
    /// </summary>
    public TimeSpan LocalCacheDefaultExpiration { get; set; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Redis 操作超时时间
    /// </summary>
    public TimeSpan RedisOperationTimeout { get; set; } = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// 是否启用指标收集
    /// </summary>
    public bool EnableMetrics { get; set; } = true;
}
