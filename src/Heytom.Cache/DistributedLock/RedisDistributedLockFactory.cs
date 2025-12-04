using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Heytom.Cache.DistributedLock;

/// <summary>
/// Redis 分布式锁工厂
/// </summary>
public class RedisDistributedLockFactory : IDistributedLockFactory
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly int _database;

    /// <summary>
    /// 初始化 RedisDistributedLockFactory 实例
    /// </summary>
    /// <param name="connectionMultiplexer">Redis 连接</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <param name="database">Redis 数据库编号（默认为 0）</param>
    public RedisDistributedLockFactory(
        IConnectionMultiplexer connectionMultiplexer,
        ILoggerFactory? loggerFactory = null,
        int database = 0)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _loggerFactory = loggerFactory;
        _database = database;
    }

    /// <summary>
    /// 创建分布式锁
    /// </summary>
    public IDistributedLock CreateLock(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Resource name cannot be null or empty", nameof(resource));
        }

        var database = _connectionMultiplexer.GetDatabase(_database);
        var logger = _loggerFactory?.CreateLogger<RedisDistributedLock>();

        return new RedisDistributedLock(database, resource, logger);
    }

    /// <summary>
    /// 创建分布式锁并尝试获取
    /// </summary>
    public IDistributedLock? CreateLockAndAcquire(string resource, TimeSpan expiry, TimeSpan? wait = null, TimeSpan? retry = null)
    {
        var lockInstance = CreateLock(resource);

        if (lockInstance.TryAcquire(expiry, wait, retry))
        {
            return lockInstance;
        }

        lockInstance.Dispose();
        return null;
    }

    /// <summary>
    /// 异步创建分布式锁并尝试获取
    /// </summary>
    public async Task<IDistributedLock?> CreateLockAndAcquireAsync(string resource, TimeSpan expiry, TimeSpan? wait = null, TimeSpan? retry = null, CancellationToken cancellationToken = default)
    {
        var lockInstance = CreateLock(resource);

        if (await lockInstance.TryAcquireAsync(expiry, wait, retry, cancellationToken).ConfigureAwait(false))
        {
            return lockInstance;
        }

        lockInstance.Dispose();
        return null;
    }
}
