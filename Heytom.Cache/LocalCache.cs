using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace Heytom.Cache;

/// <summary>
/// 本地内存缓存包装类，提供二级缓存功能
/// 实现 LRU 驱逐策略和大小限制
/// </summary>
internal class LocalCache : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly HybridCacheOptions _options;
    private readonly Dictionary<string, TimeSpan> _slidingExpirations;
    private readonly object _lock = new object();
    private bool _disposed;

    /// <summary>
    /// 初始化 LocalCache 实例
    /// </summary>
    /// <param name="options">混合缓存配置选项</param>
    public LocalCache(HybridCacheOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _slidingExpirations = new Dictionary<string, TimeSpan>();

        // 配置 MemoryCache 选项
        var memoryCacheOptions = new MemoryCacheOptions
        {
            // 设置缓存大小限制
            SizeLimit = _options.LocalCacheMaxSize,
            // 压缩间隔 - 定期清理过期项
            ExpirationScanFrequency = TimeSpan.FromMinutes(1)
        };

        _cache = new MemoryCache(memoryCacheOptions);
    }

    /// <summary>
    /// 获取缓存值
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <returns>缓存值，如果不存在则返回 null</returns>
    public byte[]? Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();

        return _cache.Get<byte[]>(key);
    }

    /// <summary>
    /// 异步获取缓存值
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="token">取消令牌</param>
    /// <returns>缓存值，如果不存在则返回 null</returns>
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        // MemoryCache 是同步的，但我们提供异步接口以保持一致性
        return Task.FromResult(Get(key));
    }

    /// <summary>
    /// 设置缓存值
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="options">缓存选项</param>
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

        var entryOptions = CreateMemoryCacheEntryOptions(options);
        _cache.Set(key, value, entryOptions);

        // 存储滑动过期时间以便 Refresh 使用
        if (options?.SlidingExpiration.HasValue == true)
        {
            lock (_lock)
            {
                _slidingExpirations[key] = options.SlidingExpiration.Value;
            }
        }
    }

    /// <summary>
    /// 异步设置缓存值
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="options">缓存选项</param>
    /// <param name="token">取消令牌</param>
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions? options = null, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 删除缓存项
    /// </summary>
    /// <param name="key">缓存键</param>
    public void Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();

        _cache.Remove(key);

        // 同时删除滑动过期元数据
        lock (_lock)
        {
            _slidingExpirations.Remove(key);
        }
    }

    /// <summary>
    /// 异步删除缓存项
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="token">取消令牌</param>
    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 刷新缓存项的过期时间（用于滑动过期）
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="slidingExpiration">滑动过期时间</param>
    public void Refresh(string key, TimeSpan? slidingExpiration = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();

        // 获取现有值
        var value = _cache.Get<byte[]>(key);
        
        if (value != null)
        {
            // 如果有滑动过期，重新设置以刷新过期时间
            TimeSpan? sliding = slidingExpiration;
            
            if (!sliding.HasValue)
            {
                lock (_lock)
                {
                    if (_slidingExpirations.TryGetValue(key, out var storedSliding))
                    {
                        sliding = storedSliding;
                    }
                }
            }

            if (sliding.HasValue)
            {
                var options = new DistributedCacheEntryOptions
                {
                    SlidingExpiration = sliding.Value
                };
                
                Set(key, value, options);
            }
        }
    }

    /// <summary>
    /// 异步刷新缓存项的过期时间
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="slidingExpiration">滑动过期时间</param>
    /// <param name="token">取消令牌</param>
    public Task RefreshAsync(string key, TimeSpan? slidingExpiration = null, CancellationToken token = default)
    {
        Refresh(key, slidingExpiration);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 清空所有缓存项
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();
        
        // MemoryCache 不提供 Clear 方法，需要重新创建实例
        // 但由于我们使用的是内部类，可以通过 Compact 来清理
        _cache.Compact(1.0); // 压缩 100% 的缓存
    }

    /// <summary>
    /// 创建 MemoryCacheEntryOptions，配置过期策略和 LRU
    /// </summary>
    /// <param name="options">分布式缓存选项</param>
    /// <returns>内存缓存条目选项</returns>
    private MemoryCacheEntryOptions CreateMemoryCacheEntryOptions(DistributedCacheEntryOptions? options)
    {
        var entryOptions = new MemoryCacheEntryOptions
        {
            // 设置大小为 1，用于 LRU 驱逐策略
            // 每个条目占用 1 个单位，总大小限制在 MemoryCacheOptions.SizeLimit
            Size = 1
        };

        if (options != null)
        {
            // 设置绝对过期时间
            if (options.AbsoluteExpiration.HasValue)
            {
                entryOptions.AbsoluteExpiration = options.AbsoluteExpiration;
            }
            else if (options.AbsoluteExpirationRelativeToNow.HasValue)
            {
                entryOptions.AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow;
            }

            // 设置滑动过期时间
            if (options.SlidingExpiration.HasValue)
            {
                entryOptions.SlidingExpiration = options.SlidingExpiration;
            }
        }
        else
        {
            // 使用默认过期时间
            entryOptions.AbsoluteExpirationRelativeToNow = _options.LocalCacheDefaultExpiration;
        }

        // 配置驱逐回调（可选，用于调试或监控）
        entryOptions.RegisterPostEvictionCallback((key, value, reason, state) =>
        {
            // 清理滑动过期元数据
            if (key is string keyStr)
            {
                lock (_lock)
                {
                    _slidingExpirations.Remove(keyStr);
                }
            }
        });

        return entryOptions;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalCache));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cache?.Dispose();
            _disposed = true;
        }
    }
}
