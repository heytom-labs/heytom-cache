namespace Heytom.Cache.Invalidation;

/// <summary>
/// 缓存失效事件
/// </summary>
public class CacheInvalidationEvent
{
    /// <summary>
    /// 缓存键
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 失效类型
    /// </summary>
    public CacheInvalidationType Type { get; set; }

    /// <summary>
    /// 事件发生时间（UTC）
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 事件来源（可选，用于调试）
    /// </summary>
    public string? Source { get; set; }
}

/// <summary>
/// 缓存失效类型
/// </summary>
public enum CacheInvalidationType
{
    /// <summary>
    /// 更新操作
    /// </summary>
    Update,

    /// <summary>
    /// 删除操作
    /// </summary>
    Remove,

    /// <summary>
    /// 过期操作
    /// </summary>
    Expire
}
