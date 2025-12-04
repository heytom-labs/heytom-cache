namespace Heytom.Cache.DistributedLock;

/// <summary>
/// 分布式锁接口
/// </summary>
public interface IDistributedLock : IDisposable
{
    /// <summary>
    /// 锁的资源名称
    /// </summary>
    string Resource { get; }

    /// <summary>
    /// 锁的唯一标识符（用于释放锁时验证所有权）
    /// </summary>
    string LockId { get; }

    /// <summary>
    /// 锁是否已获取
    /// </summary>
    bool IsAcquired { get; }

    /// <summary>
    /// 锁的过期时间
    /// </summary>
    TimeSpan Expiry { get; }

    /// <summary>
    /// 尝试获取锁（同步）
    /// </summary>
    /// <param name="expiry">锁的过期时间</param>
    /// <param name="wait">等待时间，如果为 null 则不等待</param>
    /// <param name="retry">重试间隔</param>
    /// <returns>如果成功获取锁返回 true，否则返回 false</returns>
    bool TryAcquire(TimeSpan expiry, TimeSpan? wait = null, TimeSpan? retry = null);

    /// <summary>
    /// 异步尝试获取锁
    /// </summary>
    /// <param name="expiry">锁的过期时间</param>
    /// <param name="wait">等待时间，如果为 null 则不等待</param>
    /// <param name="retry">重试间隔</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果成功获取锁返回 true，否则返回 false</returns>
    Task<bool> TryAcquireAsync(TimeSpan expiry, TimeSpan? wait = null, TimeSpan? retry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 释放锁（同步）
    /// </summary>
    /// <returns>如果成功释放锁返回 true，否则返回 false</returns>
    bool Release();

    /// <summary>
    /// 异步释放锁
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果成功释放锁返回 true，否则返回 false</returns>
    Task<bool> ReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 延长锁的过期时间（同步）
    /// </summary>
    /// <param name="expiry">新的过期时间</param>
    /// <returns>如果成功延长返回 true，否则返回 false</returns>
    bool Extend(TimeSpan expiry);

    /// <summary>
    /// 异步延长锁的过期时间
    /// </summary>
    /// <param name="expiry">新的过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果成功延长返回 true，否则返回 false</returns>
    Task<bool> ExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default);
}
