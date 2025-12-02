namespace Heytom.Cache.Invalidation;

/// <summary>
/// 缓存失效通知器接口
/// 负责发布缓存失效事件到其他服务实例
/// </summary>
public interface ICacheInvalidationNotifier
{
    /// <summary>
    /// 发布缓存失效事件
    /// </summary>
    /// <param name="event">失效事件</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否发布成功</returns>
    Task<bool> PublishAsync(CacheInvalidationEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量发布缓存失效事件
    /// </summary>
    /// <param name="events">失效事件集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功发布的事件数量</returns>
    Task<int> PublishBatchAsync(IEnumerable<CacheInvalidationEvent> events, CancellationToken cancellationToken = default);
}
