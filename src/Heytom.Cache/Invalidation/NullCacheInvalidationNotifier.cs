namespace Heytom.Cache.Invalidation;

/// <summary>
/// 空缓存失效通知器（不执行任何操作）
/// 用于禁用缓存失效通知功能的场景
/// </summary>
public class NullCacheInvalidationNotifier : ICacheInvalidationNotifier
{
    /// <summary>
    /// 单例实例
    /// </summary>
    public static readonly NullCacheInvalidationNotifier Instance = new();

    private NullCacheInvalidationNotifier()
    {
    }

    /// <summary>
    /// 发布缓存失效事件（不执行任何操作）
    /// </summary>
    public Task<bool> PublishAsync(CacheInvalidationEvent @event, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// 批量发布缓存失效事件（不执行任何操作）
    /// </summary>
    public Task<int> PublishBatchAsync(IEnumerable<CacheInvalidationEvent> events, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(events?.Count() ?? 0);
    }
}
