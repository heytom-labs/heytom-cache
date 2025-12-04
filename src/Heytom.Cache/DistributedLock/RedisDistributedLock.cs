using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Heytom.Cache.DistributedLock;

/// <summary>
/// 基于 Redis 的分布式锁实现
/// 使用 SET NX EX 命令实现，符合 Redis 官方推荐的分布式锁模式
/// </summary>
public class RedisDistributedLock : IDistributedLock
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisDistributedLock>? _logger;
    private bool _isAcquired;
    private bool _disposed;

    /// <summary>
    /// 锁的资源名称
    /// </summary>
    public string Resource { get; }

    /// <summary>
    /// 锁的唯一标识符
    /// </summary>
    public string LockId { get; }

    /// <summary>
    /// 锁是否已获取
    /// </summary>
    public bool IsAcquired => _isAcquired && !_disposed;

    /// <summary>
    /// 锁的过期时间
    /// </summary>
    public TimeSpan Expiry { get; private set; }

    /// <summary>
    /// Redis 锁的键前缀
    /// </summary>
    private const string LockKeyPrefix = "lock:";

    /// <summary>
    /// 获取 Redis 中的锁键
    /// </summary>
    private string LockKey => $"{LockKeyPrefix}{Resource}";

    /// <summary>
    /// 初始化 RedisDistributedLock 实例
    /// </summary>
    /// <param name="database">Redis 数据库</param>
    /// <param name="resource">锁的资源名称</param>
    /// <param name="logger">日志记录器</param>
    public RedisDistributedLock(IDatabase database, string resource, ILogger<RedisDistributedLock>? logger = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        LockId = Guid.NewGuid().ToString("N");
        _logger = logger;
    }

    /// <summary>
    /// 尝试获取锁（同步）
    /// </summary>
    public bool TryAcquire(TimeSpan expiry, TimeSpan? wait = null, TimeSpan? retry = null)
    {
        ThrowIfDisposed();

        if (_isAcquired)
        {
            _logger?.LogWarning("Lock {Resource} is already acquired with LockId {LockId}", Resource, LockId);
            return true;
        }

        Expiry = expiry;
        var retryInterval = retry ?? TimeSpan.FromMilliseconds(100);
        var deadline = wait.HasValue ? DateTime.UtcNow.Add(wait.Value) : DateTime.UtcNow;

        do
        {
            // 使用 SET NX EX 命令原子性地设置锁
            var acquired = _database.StringSet(
                LockKey,
                LockId,
                expiry,
                When.NotExists,
                CommandFlags.None
            );

            if (acquired)
            {
                _isAcquired = true;
                _logger?.LogDebug("Successfully acquired lock {Resource} with LockId {LockId} for {Expiry}",
                    Resource, LockId, expiry);
                return true;
            }

            if (!wait.HasValue)
            {
                _logger?.LogDebug("Failed to acquire lock {Resource}, not waiting", Resource);
                return false;
            }

            // 等待后重试
            Thread.Sleep(retryInterval);

        } while (DateTime.UtcNow < deadline);

        _logger?.LogWarning("Failed to acquire lock {Resource} after waiting {Wait}", Resource, wait);
        return false;
    }

    /// <summary>
    /// 异步尝试获取锁
    /// </summary>
    public async Task<bool> TryAcquireAsync(TimeSpan expiry, TimeSpan? wait = null, TimeSpan? retry = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_isAcquired)
        {
            _logger?.LogWarning("Lock {Resource} is already acquired with LockId {LockId}", Resource, LockId);
            return true;
        }

        Expiry = expiry;
        var retryInterval = retry ?? TimeSpan.FromMilliseconds(100);
        var deadline = wait.HasValue ? DateTime.UtcNow.Add(wait.Value) : DateTime.UtcNow;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 使用 SET NX EX 命令原子性地设置锁
            var acquired = await _database.StringSetAsync(
                LockKey,
                LockId,
                expiry,
                When.NotExists,
                CommandFlags.None
            ).ConfigureAwait(false);

            if (acquired)
            {
                _isAcquired = true;
                _logger?.LogDebug("Successfully acquired lock {Resource} with LockId {LockId} for {Expiry}",
                    Resource, LockId, expiry);
                return true;
            }

            if (!wait.HasValue)
            {
                _logger?.LogDebug("Failed to acquire lock {Resource}, not waiting", Resource);
                return false;
            }

            // 等待后重试
            await Task.Delay(retryInterval, cancellationToken).ConfigureAwait(false);

        } while (DateTime.UtcNow < deadline);

        _logger?.LogWarning("Failed to acquire lock {Resource} after waiting {Wait}", Resource, wait);
        return false;
    }

    /// <summary>
    /// 释放锁（同步）
    /// 使用 Lua 脚本确保只有锁的持有者才能释放锁
    /// </summary>
    public bool Release()
    {
        ThrowIfDisposed();

        if (!_isAcquired)
        {
            _logger?.LogWarning("Attempted to release lock {Resource} that was not acquired", Resource);
            return false;
        }

        try
        {
            // Lua 脚本：只有当锁的值等于 LockId 时才删除
            const string script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";

            var result = (int)_database.ScriptEvaluate(
                script,
                new RedisKey[] { LockKey },
                new RedisValue[] { LockId }
            );

            var released = result == 1;

            if (released)
            {
                _isAcquired = false;
                _logger?.LogDebug("Successfully released lock {Resource} with LockId {LockId}", Resource, LockId);
            }
            else
            {
                _logger?.LogWarning("Failed to release lock {Resource} with LockId {LockId}, lock may have expired or been taken by another process",
                    Resource, LockId);
            }

            return released;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error releasing lock {Resource} with LockId {LockId}", Resource, LockId);
            return false;
        }
    }

    /// <summary>
    /// 异步释放锁
    /// 使用 Lua 脚本确保只有锁的持有者才能释放锁
    /// </summary>
    public async Task<bool> ReleaseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_isAcquired)
        {
            _logger?.LogWarning("Attempted to release lock {Resource} that was not acquired", Resource);
            return false;
        }

        try
        {
            // Lua 脚本：只有当锁的值等于 LockId 时才删除
            const string script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";

            var result = (int)await _database.ScriptEvaluateAsync(
                script,
                new RedisKey[] { LockKey },
                new RedisValue[] { LockId }
            ).ConfigureAwait(false);

            var released = result == 1;

            if (released)
            {
                _isAcquired = false;
                _logger?.LogDebug("Successfully released lock {Resource} with LockId {LockId}", Resource, LockId);
            }
            else
            {
                _logger?.LogWarning("Failed to release lock {Resource} with LockId {LockId}, lock may have expired or been taken by another process",
                    Resource, LockId);
            }

            return released;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error releasing lock {Resource} with LockId {LockId}", Resource, LockId);
            return false;
        }
    }

    /// <summary>
    /// 延长锁的过期时间（同步）
    /// 使用 Lua 脚本确保只有锁的持有者才能延长锁
    /// </summary>
    public bool Extend(TimeSpan expiry)
    {
        ThrowIfDisposed();

        if (!_isAcquired)
        {
            _logger?.LogWarning("Attempted to extend lock {Resource} that was not acquired", Resource);
            return false;
        }

        try
        {
            // Lua 脚本：只有当锁的值等于 LockId 时才延长过期时间
            const string script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('pexpire', KEYS[1], ARGV[2])
                else
                    return 0
                end";

            var result = (int)_database.ScriptEvaluate(
                script,
                new RedisKey[] { LockKey },
                new RedisValue[] { LockId, (long)expiry.TotalMilliseconds }
            );

            var extended = result == 1;

            if (extended)
            {
                Expiry = expiry;
                _logger?.LogDebug("Successfully extended lock {Resource} with LockId {LockId} to {Expiry}",
                    Resource, LockId, expiry);
            }
            else
            {
                _logger?.LogWarning("Failed to extend lock {Resource} with LockId {LockId}, lock may have expired",
                    Resource, LockId);
                _isAcquired = false;
            }

            return extended;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error extending lock {Resource} with LockId {LockId}", Resource, LockId);
            return false;
        }
    }

    /// <summary>
    /// 异步延长锁的过期时间
    /// 使用 Lua 脚本确保只有锁的持有者才能延长锁
    /// </summary>
    public async Task<bool> ExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_isAcquired)
        {
            _logger?.LogWarning("Attempted to extend lock {Resource} that was not acquired", Resource);
            return false;
        }

        try
        {
            // Lua 脚本：只有当锁的值等于 LockId 时才延长过期时间
            const string script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('pexpire', KEYS[1], ARGV[2])
                else
                    return 0
                end";

            var result = (int)await _database.ScriptEvaluateAsync(
                script,
                new RedisKey[] { LockKey },
                new RedisValue[] { LockId, (long)expiry.TotalMilliseconds }
            ).ConfigureAwait(false);

            var extended = result == 1;

            if (extended)
            {
                Expiry = expiry;
                _logger?.LogDebug("Successfully extended lock {Resource} with LockId {LockId} to {Expiry}",
                    Resource, LockId, expiry);
            }
            else
            {
                _logger?.LogWarning("Failed to extend lock {Resource} with LockId {LockId}, lock may have expired",
                    Resource, LockId);
                _isAcquired = false;
            }

            return extended;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error extending lock {Resource} with LockId {LockId}", Resource, LockId);
            return false;
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_isAcquired)
        {
            try
            {
                Release();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error releasing lock {Resource} during disposal", Resource);
            }
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RedisDistributedLock));
        }
    }
}
