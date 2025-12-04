namespace Heytom.Cache.DistributedLock;

/// <summary>
/// 分布式锁工厂接口
/// </summary>
public interface IDistributedLockFactory
{
    /// <summary>
    /// 创建分布式锁
    /// </summary>
    /// <param name="resource">锁的资源名称</param>
    /// <returns>分布式锁实例</returns>
    IDistributedLock CreateLock(string resource);

    /// <summary>
    /// 创建分布式锁并尝试获取
    /// </summary>
    /// <param name="resource">锁的资源名称</param>
    /// <param name="expiry">锁的过期时间</param>
    /// <param name="wait">等待时间</param>
    /// <param name="retry">重试间隔</param>
    /// <returns>分布式锁实例，如果获取失败则返回 null</returns>
    IDistributedLock? CreateLockAndAcquire(string resource, TimeSpan expiry, TimeSpan? wait = null, TimeSpan? retry = null);

    /// <summary>
    /// 异步创建分布式锁并尝试获取
    /// </summary>
    /// <param name="resource">锁的资源名称</param>
    /// <param name="expiry">锁的过期时间</param>
    /// <param name="wait">等待时间</param>
    /// <param name="retry">重试间隔</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分布式锁实例，如果获取失败则返回 null</returns>
    Task<IDistributedLock?> CreateLockAndAcquireAsync(string resource, TimeSpan expiry, TimeSpan? wait = null, TimeSpan? retry = null, CancellationToken cancellationToken = default);
}
