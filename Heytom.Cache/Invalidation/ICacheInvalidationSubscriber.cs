namespace Heytom.Cache.Invalidation;

/// <summary>
/// 缓存失效订阅器接口
/// 负责订阅并处理来自其他服务实例的缓存失效事件
/// </summary>
public interface ICacheInvalidationSubscriber : IDisposable
{
    /// <summary>
    /// 订阅缓存失效事件
    /// </summary>
    /// <param name="handler">事件处理器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>订阅任务</returns>
    Task SubscribeAsync(Func<CacheInvalidationEvent, Task> handler, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消订阅
    /// </summary>
    /// <returns>取消订阅任务</returns>
    Task UnsubscribeAsync();

    /// <summary>
    /// 检查是否已订阅
    /// </summary>
    bool IsSubscribed { get; }
}
