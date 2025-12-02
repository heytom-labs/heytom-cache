namespace Heytom.Cache.Invalidation;

/// <summary>
/// 空缓存失效订阅器（不执行任何操作）
/// 用于禁用缓存失效订阅功能的场景
/// </summary>
public class NullCacheInvalidationSubscriber : ICacheInvalidationSubscriber
{
    /// <summary>
    /// 单例实例
    /// </summary>
    public static readonly NullCacheInvalidationSubscriber Instance = new();

    private NullCacheInvalidationSubscriber()
    {
    }

    /// <summary>
    /// 检查是否已订阅（始终返回 false）
    /// </summary>
    public bool IsSubscribed => false;

    /// <summary>
    /// 订阅缓存失效事件（不执行任何操作）
    /// </summary>
    public Task SubscribeAsync(Func<CacheInvalidationEvent, Task> handler, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 取消订阅（不执行任何操作）
    /// </summary>
    public Task UnsubscribeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 释放资源（不执行任何操作）
    /// </summary>
    public void Dispose()
    {
        // 无需释放资源
    }
}
