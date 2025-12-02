using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace Heytom.Cache;

/// <summary>
/// Redis 缓存客户端封装，提供基本的缓存操作和连接管理
/// </summary>
internal class RedisCache : IRedisExtensions, IDisposable
{
    private const string MetadataKeySuffix = ":metadata:sliding";
    
    private readonly ConnectionMultiplexer _connection;
    private readonly IDatabase _database;
    private readonly TimeSpan _operationTimeout;
    private bool _disposed;

    /// <summary>
    /// 初始化 RedisCache 实例
    /// </summary>
    /// <param name="connectionString">Redis 连接字符串</param>
    /// <param name="operationTimeout">操作超时时间</param>
    public RedisCache(string connectionString, TimeSpan operationTimeout)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Redis connection string cannot be null or empty.", nameof(connectionString));
        }

        _operationTimeout = operationTimeout;
        
        // 配置 Redis 连接选项
        var configurationOptions = ConfigurationOptions.Parse(connectionString);
        configurationOptions.ConnectTimeout = (int)operationTimeout.TotalMilliseconds;
        configurationOptions.SyncTimeout = (int)operationTimeout.TotalMilliseconds;
        configurationOptions.AbortOnConnectFail = false; // 允许在连接失败时继续
        
        // 创建连接（使用连接池）
        _connection = ConnectionMultiplexer.Connect(configurationOptions);
        _database = _connection.GetDatabase();
    }

    /// <summary>
    /// 获取缓存值
    /// </summary>
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();
        token.ThrowIfCancellationRequested();
        
        // 使用 Task.Run 包装以支持 CancellationToken
        var value = await Task.Run(async () => await _database.StringGetAsync(key).ConfigureAwait(false), token).ConfigureAwait(false);
        return value.HasValue ? (byte[]?)value : null;
    }

    /// <summary>
    /// 同步获取缓存值
    /// </summary>
    public byte[]? Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();
        
        var value = _database.StringGet(key);
        return value.HasValue ? (byte[]?)value : null;
    }

    /// <summary>
    /// 设置缓存值
    /// </summary>
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions? options = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        ThrowIfDisposed();
        token.ThrowIfCancellationRequested();

        var expiration = GetExpiration(options);
        
        // 设置主缓存值
        if (expiration.HasValue)
        {
            await Task.Run(async () => await _database.StringSetAsync(key, value, expiration.Value).ConfigureAwait(false), token).ConfigureAwait(false);
        }
        else
        {
            await Task.Run(async () => await _database.StringSetAsync(key, value).ConfigureAwait(false), token).ConfigureAwait(false);
        }

        // 如果有滑动过期，存储元数据以便 Refresh 使用
        if (options?.SlidingExpiration.HasValue == true)
        {
            var metadataKey = GetMetadataKey(key);
            var slidingSeconds = (long)options.SlidingExpiration.Value.TotalSeconds;
            
            if (expiration.HasValue)
            {
                await Task.Run(async () => await _database.StringSetAsync(metadataKey, slidingSeconds, expiration.Value).ConfigureAwait(false), token).ConfigureAwait(false);
            }
            else
            {
                await Task.Run(async () => await _database.StringSetAsync(metadataKey, slidingSeconds).ConfigureAwait(false), token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 同步设置缓存值
    /// </summary>
    public void Set(string key, byte[] value, DistributedCacheEntryOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        ThrowIfDisposed();

        var expiration = GetExpiration(options);
        
        // 设置主缓存值
        if (expiration.HasValue)
        {
            _database.StringSet(key, value, expiration.Value);
        }
        else
        {
            _database.StringSet(key, value);
        }

        // 如果有滑动过期，存储元数据以便 Refresh 使用
        if (options?.SlidingExpiration.HasValue == true)
        {
            var metadataKey = GetMetadataKey(key);
            var slidingSeconds = (long)options.SlidingExpiration.Value.TotalSeconds;
            
            if (expiration.HasValue)
            {
                _database.StringSet(metadataKey, slidingSeconds, expiration.Value);
            }
            else
            {
                _database.StringSet(metadataKey, slidingSeconds);
            }
        }
    }

    /// <summary>
    /// 删除缓存项
    /// </summary>
    public async Task<bool> RemoveAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();
        token.ThrowIfCancellationRequested();
        
        // 删除主键和元数据键
        var metadataKey = GetMetadataKey(key);
        await Task.Run(async () => await _database.KeyDeleteAsync(new RedisKey[] { key, metadataKey }).ConfigureAwait(false), token).ConfigureAwait(false);
        
        return true;
    }

    /// <summary>
    /// 同步删除缓存项
    /// </summary>
    public bool Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();
        
        // 删除主键和元数据键
        var metadataKey = GetMetadataKey(key);
        _database.KeyDelete(new RedisKey[] { key, metadataKey });
        
        return true;
    }

    /// <summary>
    /// 刷新缓存项的过期时间（用于滑动过期）
    /// </summary>
    public async Task<bool> RefreshAsync(string key, TimeSpan? slidingExpiration = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();
        token.ThrowIfCancellationRequested();

        // 如果没有提供滑动过期时间，尝试从元数据中获取
        if (!slidingExpiration.HasValue)
        {
            var metadataKey = GetMetadataKey(key);
            var storedSeconds = await Task.Run(async () => await _database.StringGetAsync(metadataKey).ConfigureAwait(false), token).ConfigureAwait(false);
            
            if (storedSeconds.HasValue && storedSeconds.TryParse(out long seconds))
            {
                slidingExpiration = TimeSpan.FromSeconds(seconds);
            }
        }

        if (slidingExpiration.HasValue)
        {
            // 重置主键的过期时间
            var result = await Task.Run(async () => await _database.KeyExpireAsync(key, slidingExpiration.Value).ConfigureAwait(false), token).ConfigureAwait(false);
            
            // 同时重置元数据键的过期时间
            var metadataKey = GetMetadataKey(key);
            await Task.Run(async () => await _database.KeyExpireAsync(metadataKey, slidingExpiration.Value).ConfigureAwait(false), token).ConfigureAwait(false);
            
            return result;
        }

        return false;
    }

    /// <summary>
    /// 同步刷新缓存项的过期时间
    /// </summary>
    public bool Refresh(string key, TimeSpan? slidingExpiration = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();

        // 如果没有提供滑动过期时间，尝试从元数据中获取
        if (!slidingExpiration.HasValue)
        {
            var metadataKey = GetMetadataKey(key);
            var storedSeconds = _database.StringGet(metadataKey);
            
            if (storedSeconds.HasValue && storedSeconds.TryParse(out long seconds))
            {
                slidingExpiration = TimeSpan.FromSeconds(seconds);
            }
        }

        if (slidingExpiration.HasValue)
        {
            // 重置主键的过期时间
            var result = _database.KeyExpire(key, slidingExpiration.Value);
            
            // 同时重置元数据键的过期时间
            var metadataKey = GetMetadataKey(key);
            _database.KeyExpire(metadataKey, slidingExpiration.Value);
            
            return result;
        }

        return false;
    }

    /// <summary>
    /// 检查 Redis 连接是否正常
    /// </summary>
    public bool IsConnected => _connection.IsConnected;

    /// <summary>
    /// 获取数据库实例（用于扩展功能）
    /// </summary>
    internal IDatabase Database => _database;

    /// <summary>
    /// 获取连接实例（用于 Pub/Sub）
    /// </summary>
    internal ConnectionMultiplexer Connection => _connection;

    /// <summary>
    /// 获取元数据键名
    /// </summary>
    private static string GetMetadataKey(string key)
    {
        return $"{key}{MetadataKeySuffix}";
    }

    /// <summary>
    /// 计算过期时间
    /// </summary>
    private TimeSpan? GetExpiration(DistributedCacheEntryOptions? options)
    {
        if (options == null)
        {
            return null;
        }

        // 处理绝对过期时间
        if (options.AbsoluteExpiration.HasValue)
        {
            var absoluteExpiration = options.AbsoluteExpiration.Value - DateTimeOffset.UtcNow;
            
            // 如果同时设置了滑动过期，取较小值
            if (options.SlidingExpiration.HasValue)
            {
                return absoluteExpiration < options.SlidingExpiration.Value 
                    ? absoluteExpiration 
                    : options.SlidingExpiration.Value;
            }
            
            return absoluteExpiration;
        }

        // 处理相对绝对过期时间
        if (options.AbsoluteExpirationRelativeToNow.HasValue)
        {
            // 如果同时设置了滑动过期，取较小值
            if (options.SlidingExpiration.HasValue)
            {
                return options.AbsoluteExpirationRelativeToNow.Value < options.SlidingExpiration.Value
                    ? options.AbsoluteExpirationRelativeToNow.Value
                    : options.SlidingExpiration.Value;
            }
            
            return options.AbsoluteExpirationRelativeToNow.Value;
        }

        // 仅滑动过期
        if (options.SlidingExpiration.HasValue)
        {
            return options.SlidingExpiration.Value;
        }

        return null;
    }

    #region IRedisExtensions Implementation

    // Hash operations
    
    /// <summary>
    /// 设置 Hash 字段值
    /// </summary>
    public async Task<bool> HashSetAsync(string key, string field, byte[] value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("Field cannot be null or empty.", nameof(field));
        }

        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        ThrowIfDisposed();
        
        return await _database.HashSetAsync(key, field, value).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取 Hash 字段值
    /// </summary>
    public async Task<byte[]?> HashGetAsync(string key, string field)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("Field cannot be null or empty.", nameof(field));
        }

        ThrowIfDisposed();
        
        var value = await _database.HashGetAsync(key, field).ConfigureAwait(false);
        return value.HasValue ? (byte[]?)value : null;
    }

    /// <summary>
    /// 获取 Hash 所有字段和值
    /// </summary>
    public async Task<Dictionary<string, byte[]>> HashGetAllAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();
        
        var entries = await _database.HashGetAllAsync(key).ConfigureAwait(false);
        var result = new Dictionary<string, byte[]>();
        
        foreach (var entry in entries)
        {
            result[entry.Name.ToString()] = (byte[])entry.Value!;
        }
        
        return result;
    }

    /// <summary>
    /// 删除 Hash 字段
    /// </summary>
    public async Task<bool> HashDeleteAsync(string key, string field)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("Field cannot be null or empty.", nameof(field));
        }

        ThrowIfDisposed();
        
        return await _database.HashDeleteAsync(key, field).ConfigureAwait(false);
    }

    // List operations
    
    /// <summary>
    /// 将值推入 List 尾部
    /// </summary>
    public async Task<long> ListPushAsync(string key, byte[] value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        ThrowIfDisposed();
        
        return await _database.ListRightPushAsync(key, value).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 List 头部弹出值
    /// </summary>
    public async Task<byte[]?> ListPopAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();
        
        var value = await _database.ListLeftPopAsync(key).ConfigureAwait(false);
        return value.HasValue ? (byte[]?)value : null;
    }

    /// <summary>
    /// 获取 List 长度
    /// </summary>
    public async Task<long> ListLengthAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();
        
        return await _database.ListLengthAsync(key).ConfigureAwait(false);
    }

    // Set operations
    
    /// <summary>
    /// 添加值到 Set
    /// </summary>
    public async Task<bool> SetAddAsync(string key, byte[] value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        ThrowIfDisposed();
        
        return await _database.SetAddAsync(key, value).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 Set 移除值
    /// </summary>
    public async Task<bool> SetRemoveAsync(string key, byte[] value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        ThrowIfDisposed();
        
        return await _database.SetRemoveAsync(key, value).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取 Set 所有成员
    /// </summary>
    public async Task<byte[][]> SetMembersAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();
        
        var members = await _database.SetMembersAsync(key).ConfigureAwait(false);
        return members.Select(m => (byte[])m!).ToArray();
    }

    // Sorted Set operations
    
    /// <summary>
    /// 添加值到 Sorted Set
    /// </summary>
    public async Task<bool> SortedSetAddAsync(string key, byte[] value, double score)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        ThrowIfDisposed();
        
        return await _database.SortedSetAddAsync(key, value, score).ConfigureAwait(false);
    }

    /// <summary>
    /// 按分数范围获取 Sorted Set 成员
    /// </summary>
    public async Task<byte[][]> SortedSetRangeByScoreAsync(string key, double min, double max)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();
        
        var members = await _database.SortedSetRangeByScoreAsync(key, min, max).ConfigureAwait(false);
        return members.Select(m => (byte[])m!).ToArray();
    }

    // Pub/Sub operations
    
    /// <summary>
    /// 发布消息到频道
    /// </summary>
    public async Task PublishAsync(string channel, byte[] message)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ArgumentException("Channel cannot be null or empty.", nameof(channel));
        }

        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        ThrowIfDisposed();
        
        var subscriber = _connection.GetSubscriber();
        await subscriber.PublishAsync(RedisChannel.Literal(channel), message).ConfigureAwait(false);
    }

    /// <summary>
    /// 订阅频道消息
    /// </summary>
    public async Task SubscribeAsync(string channel, Action<byte[]> handler)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ArgumentException("Channel cannot be null or empty.", nameof(channel));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        ThrowIfDisposed();
        
        var subscriber = _connection.GetSubscriber();
        await subscriber.SubscribeAsync(RedisChannel.Literal(channel), (ch, message) =>
        {
            if (message.HasValue)
            {
                handler((byte[])message!);
            }
        }).ConfigureAwait(false);
    }

    #endregion

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RedisCache));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection?.Dispose();
            _disposed = true;
        }
    }
}
