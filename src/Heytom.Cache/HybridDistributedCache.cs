using Heytom.Cache.Invalidation;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;

namespace Heytom.Cache;

/// <summary>
/// 混合分布式缓存实现，结合本地缓存和 Redis 缓存
/// 实现 IDistributedCache 接口和 IRedisExtensions 接口
/// </summary>
public class HybridDistributedCache : IDistributedCache, IRedisExtensions, IDisposable
{
    private readonly RedisCache _redisCache;
    private readonly LocalCache? _localCache;
    private readonly HybridCacheOptions _options;
    private readonly ILogger<HybridDistributedCache>? _logger;
    private readonly MetricsCollector? _metricsCollector;
    private readonly ICacheInvalidationNotifier? _invalidationNotifier;
    private readonly ICacheInvalidationSubscriber? _invalidationSubscriber;
    private readonly ResiliencePipeline<byte[]?> _asyncGetPolicy;
    private readonly ResiliencePipeline<object?> _asyncSetPolicy;
    private readonly ResiliencePipeline<object?> _asyncRemovePolicy;
    private readonly ResiliencePipeline _syncGetPolicy;
    private readonly ResiliencePipeline _syncSetPolicy;
    private readonly ResiliencePipeline _syncRemovePolicy;
    private bool _disposed;

    /// <summary>
    /// 初始化 HybridDistributedCache 实例
    /// </summary>
    /// <param name="options">混合缓存配置选项</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="invalidationNotifier">缓存失效通知器（可选）</param>
    /// <param name="invalidationSubscriber">缓存失效订阅器（可选）</param>
    public HybridDistributedCache(
        HybridCacheOptions options,
        ILogger<HybridDistributedCache>? logger = null,
        ICacheInvalidationNotifier? invalidationNotifier = null,
        ICacheInvalidationSubscriber? invalidationSubscriber = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        // 初始化 Redis 缓存
        _redisCache = new RedisCache(
            options.RedisConnectionString,
            options.RedisOperationTimeout);

        // 如果启用本地缓存，则初始化本地缓存
        if (options.EnableLocalCache)
        {
            _localCache = new LocalCache(options);
        }

        // 如果启用指标收集，则初始化指标收集器
        if (options.EnableMetrics)
        {
            _metricsCollector = new MetricsCollector();
        }

        // 初始化缓存失效通知/订阅机制
        if (options.EnableCacheInvalidation && options.EnableLocalCache)
        {
            _invalidationNotifier = invalidationNotifier;
            _invalidationSubscriber = invalidationSubscriber;

            // 订阅缓存失效事件
            if (_invalidationSubscriber != null && !_invalidationSubscriber.IsSubscribed)
            {
                _ = SubscribeToInvalidationEventsAsync();
            }
        }

        // 初始化弹性策略（重试 + 断路器）
        _asyncGetPolicy = ResiliencePolicies.CreateAsyncCombinedPolicy<byte[]?>();
        _asyncSetPolicy = ResiliencePolicies.CreateAsyncCombinedPolicy<object?>();
        _asyncRemovePolicy = ResiliencePolicies.CreateAsyncCombinedPolicy<object?>();
        _syncGetPolicy = ResiliencePolicies.CreateSyncCombinedPolicy();
        _syncSetPolicy = ResiliencePolicies.CreateSyncCombinedPolicy();
        _syncRemovePolicy = ResiliencePolicies.CreateSyncCombinedPolicy();

        _logger?.LogInformation(
            "HybridDistributedCache initialized. LocalCache: {LocalCacheEnabled}, Redis: {RedisConnection}, Metrics: {MetricsEnabled}",
            options.EnableLocalCache,
            options.RedisConnectionString,
            options.EnableMetrics);
    }

    #region IDistributedCache Implementation

    /// <summary>
    /// 获取缓存值（同步）
    /// 优先从本地缓存获取，如果不存在则从 Redis 获取并填充本地缓存
    /// 使用重试和断路器策略处理 Redis 连接失败
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

        if (_metricsCollector != null)
        {
            return _metricsCollector.MeasureOperation(
                () => GetInternal(key),
                (elapsed, result) =>
                {
                    if (result != null)
                    {
                        // 判断是从本地缓存还是 Redis 命中
                        bool isLocalHit = _localCache?.Get(key) != null;
                        _metricsCollector.RecordHit(isLocalHit, elapsed);
                    }
                    else
                    {
                        _metricsCollector.RecordMiss(elapsed);
                    }
                });
        }

        return GetInternal(key);
    }

    private byte[]? GetInternal(string key)
    {
        // 如果启用本地缓存，优先从本地缓存获取
        if (_localCache != null)
        {
            var localValue = _localCache.Get(key);
            if (localValue != null)
            {
                _logger?.LogDebug("Cache hit in local cache for key: {Key}", key);
                return localValue;
            }

            _logger?.LogDebug("Cache miss in local cache for key: {Key}", key);
        }

        try
        {
            // 使用弹性策略从 Redis 获取（重试 + 断路器）
            var redisValue = _syncGetPolicy.Execute(() => _redisCache.Get(key));
            
            if (redisValue != null)
            {
                _logger?.LogDebug("Cache hit in Redis for key: {Key}", key);
                
                // 填充本地缓存
                if (_localCache != null)
                {
                    _localCache.Set(key, redisValue);
                    _logger?.LogDebug("Populated local cache for key: {Key}", key);
                }
            }
            else
            {
                _logger?.LogDebug("Cache miss in Redis for key: {Key}", key);
            }

            return redisValue;
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or TimeoutException or BrokenCircuitException)
        {
            _logger?.LogError(ex, "Redis operation failed for key: {Key}", key);
            
            // 降级到本地缓存（如果可用）
            if (_localCache != null)
            {
                _logger?.LogWarning("Degrading to local cache due to Redis failure for key: {Key}", key);
                return _localCache.Get(key);
            }
            
            // 如果本地缓存未启用，抛出明确的异常
            _logger?.LogError("Redis unavailable and local cache is disabled. Cannot retrieve key: {Key}", key);
            throw new InvalidOperationException(
                $"Redis is unavailable and local cache is disabled. Cannot retrieve cache value for key: {key}", 
                ex);
        }
    }

    /// <summary>
    /// 异步获取缓存值
    /// 优先从本地缓存获取，如果不存在则从 Redis 获取并填充本地缓存
    /// 使用重试和断路器策略处理 Redis 连接失败
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="token">取消令牌</param>
    /// <returns>缓存值，如果不存在则返回 null</returns>
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();

        if (_metricsCollector != null)
        {
            return await _metricsCollector.MeasureOperationAsync(
                () => GetAsyncInternal(key, token),
                (elapsed, result) =>
                {
                    if (result != null)
                    {
                        // 判断是从本地缓存还是 Redis 命中
                        // 注意：这里简化处理，实际中需要在 GetAsyncInternal 中跟踪来源
                        bool isLocalHit = _localCache?.Get(key) != null;
                        _metricsCollector.RecordHit(isLocalHit, elapsed);
                    }
                    else
                    {
                        _metricsCollector.RecordMiss(elapsed);
                    }
                }).ConfigureAwait(false);
        }

        return await GetAsyncInternal(key, token).ConfigureAwait(false);
    }

    private async Task<byte[]?> GetAsyncInternal(string key, CancellationToken token)
    {
        // 如果启用本地缓存，优先从本地缓存获取
        if (_localCache != null)
        {
            var localValue = await _localCache.GetAsync(key, token).ConfigureAwait(false);
            if (localValue != null)
            {
                _logger?.LogDebug("Cache hit in local cache for key: {Key}", key);
                return localValue;
            }

            _logger?.LogDebug("Cache miss in local cache for key: {Key}", key);
        }

        try
        {
            // 使用弹性策略从 Redis 获取（重试 + 断路器）
            var redisValue = await _asyncGetPolicy.ExecuteAsync(
                async ct => await _redisCache.GetAsync(key, ct).ConfigureAwait(false),
                token).ConfigureAwait(false);
            
            if (redisValue != null)
            {
                _logger?.LogDebug("Cache hit in Redis for key: {Key}", key);
                
                // 填充本地缓存
                if (_localCache != null)
                {
                    await _localCache.SetAsync(key, redisValue, token: token).ConfigureAwait(false);
                    _logger?.LogDebug("Populated local cache for key: {Key}", key);
                }
            }
            else
            {
                _logger?.LogDebug("Cache miss in Redis for key: {Key}", key);
            }

            return redisValue;
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or TimeoutException or BrokenCircuitException)
        {
            _logger?.LogError(ex, "Redis operation failed for key: {Key}", key);
            
            // 降级到本地缓存（如果可用）
            if (_localCache != null)
            {
                _logger?.LogWarning("Degrading to local cache due to Redis failure for key: {Key}", key);
                return await _localCache.GetAsync(key, token).ConfigureAwait(false);
            }
            
            // 如果本地缓存未启用，抛出明确的异常
            _logger?.LogError("Redis unavailable and local cache is disabled. Cannot retrieve key: {Key}", key);
            throw new InvalidOperationException(
                $"Redis is unavailable and local cache is disabled. Cannot retrieve cache value for key: {key}", 
                ex);
        }
    }

    /// <summary>
    /// 设置缓存值（同步）
    /// 同时更新 Redis 和本地缓存（双写一致性）
    /// 使用重试和断路器策略处理 Redis 连接失败
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="options">缓存选项</param>
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
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

        if (_metricsCollector != null)
        {
            _metricsCollector.MeasureOperation(
                () =>
                {
                    SetInternal(key, value, options);
                    return true;
                },
                (elapsed, _) => _metricsCollector.RecordOperation(elapsed));
        }
        else
        {
            SetInternal(key, value, options);
        }
    }

    private void SetInternal(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        try
        {
            // 使用弹性策略更新 Redis（重试 + 断路器）
            _syncSetPolicy.Execute(() => _redisCache.Set(key, value, options));
            _logger?.LogDebug("Set cache value in Redis for key: {Key}", key);

            // 同时更新本地缓存
            if (_localCache != null)
            {
                _localCache.Set(key, value, options);
                _logger?.LogDebug("Set cache value in local cache for key: {Key}", key);
            }

            // 发布缓存失效通知（异步，不阻塞主流程）
            _ = PublishInvalidationEventAsync(key, CacheInvalidationType.Update);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or TimeoutException or BrokenCircuitException)
        {
            _logger?.LogError(ex, "Redis operation failed for key: {Key}", key);
            
            // 降级：仅更新本地缓存（如果可用）
            if (_localCache != null)
            {
                _logger?.LogWarning("Degrading to local cache only due to Redis failure for key: {Key}", key);
                _localCache.Set(key, value, options);
                return;
            }
            
            // 如果本地缓存未启用，抛出明确的异常
            _logger?.LogError("Redis unavailable and local cache is disabled. Cannot set key: {Key}", key);
            throw new InvalidOperationException(
                $"Redis is unavailable and local cache is disabled. Cannot set cache value for key: {key}", 
                ex);
        }
    }

    /// <summary>
    /// 异步设置缓存值
    /// 同时更新 Redis 和本地缓存（双写一致性）
    /// 使用重试和断路器策略处理 Redis 连接失败
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    /// <param name="options">缓存选项</param>
    /// <param name="token">取消令牌</param>
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
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

        if (_metricsCollector != null)
        {
            await _metricsCollector.MeasureOperationAsync(
                async () =>
                {
                    await SetAsyncInternal(key, value, options, token).ConfigureAwait(false);
                    return true;
                },
                (elapsed, _) => _metricsCollector.RecordOperation(elapsed)).ConfigureAwait(false);
        }
        else
        {
            await SetAsyncInternal(key, value, options, token).ConfigureAwait(false);
        }
    }

    private async Task SetAsyncInternal(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token)
    {
        try
        {
            // 使用弹性策略更新 Redis（重试 + 断路器）
            await _asyncSetPolicy.ExecuteAsync(
                async ct => 
                {
                    await _redisCache.SetAsync(key, value, options, ct).ConfigureAwait(false);
                    return (object?)null;
                },
                token).ConfigureAwait(false);
            _logger?.LogDebug("Set cache value in Redis for key: {Key}", key);

            // 同时更新本地缓存
            if (_localCache != null)
            {
                await _localCache.SetAsync(key, value, options, token).ConfigureAwait(false);
                _logger?.LogDebug("Set cache value in local cache for key: {Key}", key);
            }

            // 发布缓存失效通知
            await PublishInvalidationEventAsync(key, CacheInvalidationType.Update).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or TimeoutException or BrokenCircuitException)
        {
            _logger?.LogError(ex, "Redis operation failed for key: {Key}", key);
            
            // 降级：仅更新本地缓存（如果可用）
            if (_localCache != null)
            {
                _logger?.LogWarning("Degrading to local cache only due to Redis failure for key: {Key}", key);
                await _localCache.SetAsync(key, value, options, token).ConfigureAwait(false);
                return;
            }
            
            // 如果本地缓存未启用，抛出明确的异常
            _logger?.LogError("Redis unavailable and local cache is disabled. Cannot set key: {Key}", key);
            throw new InvalidOperationException(
                $"Redis is unavailable and local cache is disabled. Cannot set cache value for key: {key}", 
                ex);
        }
    }

    /// <summary>
    /// 刷新缓存项的过期时间（同步）
    /// 用于滑动过期策略
    /// </summary>
    /// <param name="key">缓存键</param>
    public void Refresh(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();

        try
        {
            // 刷新 Redis 中的过期时间
            // 注意：这需要知道原始的滑动过期时间，这里我们尝试从 Redis 获取 TTL
            _redisCache.Refresh(key);
            _logger?.LogDebug("Refreshed cache in Redis for key: {Key}", key);

            // 刷新本地缓存
            if (_localCache != null)
            {
                _localCache.Refresh(key);
                _logger?.LogDebug("Refreshed cache in local cache for key: {Key}", key);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error refreshing cache for key: {Key}", key);
            throw;
        }
    }

    /// <summary>
    /// 异步刷新缓存项的过期时间
    /// 用于滑动过期策略
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="token">取消令牌</param>
    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();

        try
        {
            // 刷新 Redis 中的过期时间
            await _redisCache.RefreshAsync(key, token: token).ConfigureAwait(false);
            _logger?.LogDebug("Refreshed cache in Redis for key: {Key}", key);

            // 刷新本地缓存
            if (_localCache != null)
            {
                await _localCache.RefreshAsync(key, token: token).ConfigureAwait(false);
                _logger?.LogDebug("Refreshed cache in local cache for key: {Key}", key);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error refreshing cache for key: {Key}", key);
            throw;
        }
    }

    /// <summary>
    /// 删除缓存项（同步）
    /// 同时从 Redis 和本地缓存删除（双写一致性）
    /// 使用重试和断路器策略处理 Redis 连接失败
    /// </summary>
    /// <param name="key">缓存键</param>
    public void Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();

        try
        {
            // 使用弹性策略从 Redis 删除（重试 + 断路器）
            _syncRemovePolicy.Execute(() => _redisCache.Remove(key));
            _logger?.LogDebug("Removed cache value from Redis for key: {Key}", key);

            // 同时从本地缓存删除
            if (_localCache != null)
            {
                _localCache.Remove(key);
                _logger?.LogDebug("Removed cache value from local cache for key: {Key}", key);
            }

            // 发布缓存失效通知（异步，不阻塞主流程）
            _ = PublishInvalidationEventAsync(key, CacheInvalidationType.Remove);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or TimeoutException or BrokenCircuitException)
        {
            _logger?.LogError(ex, "Redis operation failed for key: {Key}", key);
            
            // 降级：仅从本地缓存删除（如果可用）
            if (_localCache != null)
            {
                _logger?.LogWarning("Degrading to local cache only due to Redis failure for key: {Key}", key);
                _localCache.Remove(key);
                return;
            }
            
            // 如果本地缓存未启用，抛出明确的异常
            _logger?.LogError("Redis unavailable and local cache is disabled. Cannot remove key: {Key}", key);
            throw new InvalidOperationException(
                $"Redis is unavailable and local cache is disabled. Cannot remove cache value for key: {key}", 
                ex);
        }
    }

    /// <summary>
    /// 异步删除缓存项
    /// 同时从 Redis 和本地缓存删除（双写一致性）
    /// 使用重试和断路器策略处理 Redis 连接失败
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="token">取消令牌</param>
    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        }

        ThrowIfDisposed();

        try
        {
            // 使用弹性策略从 Redis 删除（重试 + 断路器）
            await _asyncRemovePolicy.ExecuteAsync(
                async ct => 
                {
                    await _redisCache.RemoveAsync(key, ct).ConfigureAwait(false);
                    return (object?)null;
                },
                token).ConfigureAwait(false);
            _logger?.LogDebug("Removed cache value from Redis for key: {Key}", key);

            // 同时从本地缓存删除
            if (_localCache != null)
            {
                await _localCache.RemoveAsync(key, token).ConfigureAwait(false);
                _logger?.LogDebug("Removed cache value from local cache for key: {Key}", key);
            }

            // 发布缓存失效通知
            await PublishInvalidationEventAsync(key, CacheInvalidationType.Remove).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or TimeoutException or BrokenCircuitException)
        {
            _logger?.LogError(ex, "Redis operation failed for key: {Key}", key);
            
            // 降级：仅从本地缓存删除（如果可用）
            if (_localCache != null)
            {
                _logger?.LogWarning("Degrading to local cache only due to Redis failure for key: {Key}", key);
                await _localCache.RemoveAsync(key, token).ConfigureAwait(false);
                return;
            }
            
            // 如果本地缓存未启用，抛出明确的异常
            _logger?.LogError("Redis unavailable and local cache is disabled. Cannot remove key: {Key}", key);
            throw new InvalidOperationException(
                $"Redis is unavailable and local cache is disabled. Cannot remove cache value for key: {key}", 
                ex);
        }
    }

    #endregion

    #region IRedisExtensions Implementation

    // 将 Redis 扩展功能委托给 RedisCache

    public Task<bool> HashSetAsync(string key, string field, byte[] value)
    {
        ThrowIfDisposed();
        return _redisCache.HashSetAsync(key, field, value);
    }

    public Task<byte[]?> HashGetAsync(string key, string field)
    {
        ThrowIfDisposed();
        return _redisCache.HashGetAsync(key, field);
    }

    public Task<Dictionary<string, byte[]>> HashGetAllAsync(string key)
    {
        ThrowIfDisposed();
        return _redisCache.HashGetAllAsync(key);
    }

    public Task<bool> HashDeleteAsync(string key, string field)
    {
        ThrowIfDisposed();
        return _redisCache.HashDeleteAsync(key, field);
    }

    public Task<long> ListPushAsync(string key, byte[] value)
    {
        ThrowIfDisposed();
        return _redisCache.ListPushAsync(key, value);
    }

    public Task<byte[]?> ListPopAsync(string key)
    {
        ThrowIfDisposed();
        return _redisCache.ListPopAsync(key);
    }

    public Task<long> ListLengthAsync(string key)
    {
        ThrowIfDisposed();
        return _redisCache.ListLengthAsync(key);
    }

    public Task<bool> SetAddAsync(string key, byte[] value)
    {
        ThrowIfDisposed();
        return _redisCache.SetAddAsync(key, value);
    }

    public Task<bool> SetRemoveAsync(string key, byte[] value)
    {
        ThrowIfDisposed();
        return _redisCache.SetRemoveAsync(key, value);
    }

    public Task<byte[][]> SetMembersAsync(string key)
    {
        ThrowIfDisposed();
        return _redisCache.SetMembersAsync(key);
    }

    public Task<bool> SortedSetAddAsync(string key, byte[] value, double score)
    {
        ThrowIfDisposed();
        return _redisCache.SortedSetAddAsync(key, value, score);
    }

    public Task<byte[][]> SortedSetRangeByScoreAsync(string key, double min, double max)
    {
        ThrowIfDisposed();
        return _redisCache.SortedSetRangeByScoreAsync(key, min, max);
    }

    public Task PublishAsync(string channel, byte[] message)
    {
        ThrowIfDisposed();
        return _redisCache.PublishAsync(channel, message);
    }

    public Task SubscribeAsync(string channel, Action<byte[]> handler)
    {
        ThrowIfDisposed();
        return _redisCache.SubscribeAsync(channel, handler);
    }

    #endregion

    #region Metrics

    /// <summary>
    /// 获取缓存性能指标
    /// </summary>
    /// <returns>缓存指标，如果未启用指标收集则返回空指标</returns>
    public CacheMetrics GetMetrics()
    {
        return _metricsCollector?.GetMetrics() ?? new CacheMetrics();
    }

    /// <summary>
    /// 重置所有性能指标
    /// </summary>
    public void ResetMetrics()
    {
        _metricsCollector?.Reset();
        _logger?.LogInformation("Cache metrics reset");
    }

    /// <summary>
    /// 检查本地缓存是否启用
    /// </summary>
    /// <returns>如果本地缓存启用则返回 true，否则返回 false</returns>
    public bool IsLocalCacheEnabled()
    {
        return _localCache != null;
    }

    #endregion

    #region Cache Invalidation

    /// <summary>
    /// 订阅缓存失效事件
    /// </summary>
    private async Task SubscribeToInvalidationEventsAsync()
    {
        if (_invalidationSubscriber == null)
        {
            return;
        }

        try
        {
            await _invalidationSubscriber.SubscribeAsync(HandleInvalidationEventAsync).ConfigureAwait(false);
            
            _logger?.LogInformation("Successfully subscribed to cache invalidation events");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to subscribe to cache invalidation events");
        }
    }

    /// <summary>
    /// 处理缓存失效事件
    /// </summary>
    private Task HandleInvalidationEventAsync(CacheInvalidationEvent @event)
    {
        if (@event == null || string.IsNullOrWhiteSpace(@event.Key))
        {
            return Task.CompletedTask;
        }

        try
        {
            // 只删除本地缓存，不再次发布失效消息（避免循环）
            _localCache?.Remove(@event.Key);
            
            _logger?.LogDebug(
                "Invalidated local cache for key: {Key}, Type: {Type}, Source: {Source}",
                @event.Key,
                @event.Type,
                @event.Source);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling cache invalidation event for key: {Key}", @event.Key);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 发布缓存失效通知
    /// </summary>
    private async Task PublishInvalidationEventAsync(string key, CacheInvalidationType type)
    {
        if (_invalidationNotifier == null || !_options.EnableCacheInvalidation)
        {
            return;
        }

        try
        {
            var @event = new CacheInvalidationEvent
            {
                Key = key,
                Type = type,
                Timestamp = DateTimeOffset.UtcNow,
                Source = Environment.MachineName // 可选：用于调试
            };

            await _invalidationNotifier.PublishAsync(@event).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 发布失败不应影响主流程
            _logger?.LogWarning(ex, "Failed to publish cache invalidation event for key: {Key}", key);
        }
    }

    #endregion

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HybridDistributedCache));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _invalidationSubscriber?.Dispose();
            _redisCache?.Dispose();
            _localCache?.Dispose();
            _disposed = true;
            
            _logger?.LogInformation("HybridDistributedCache disposed");
        }
    }
}
