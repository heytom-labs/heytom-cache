using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Heytom.Cache.Invalidation;

/// <summary>
/// 基于 Redis Pub/Sub 的缓存失效订阅器实现
/// </summary>
public class RedisCacheInvalidationSubscriber : ICacheInvalidationSubscriber
{
    private const string DefaultChannel = "heytom:cache:invalidation";
    
    private readonly IConnectionMultiplexer _connection;
    private readonly ILogger<RedisCacheInvalidationSubscriber>? _logger;
    private readonly string _channel;
    private readonly JsonSerializerOptions _jsonOptions;
    private Func<CacheInvalidationEvent, Task>? _handler;
    private bool _isSubscribed;
    private bool _disposed;

    /// <summary>
    /// 初始化 Redis 缓存失效订阅器
    /// </summary>
    /// <param name="connection">Redis 连接</param>
    /// <param name="channel">订阅频道（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public RedisCacheInvalidationSubscriber(
        IConnectionMultiplexer connection,
        string? channel = null,
        ILogger<RedisCacheInvalidationSubscriber>? logger = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _channel = channel ?? DefaultChannel;
        _logger = logger;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    /// <summary>
    /// 检查是否已订阅
    /// </summary>
    public bool IsSubscribed => _isSubscribed;

    /// <summary>
    /// 订阅缓存失效事件
    /// </summary>
    public async Task SubscribeAsync(Func<CacheInvalidationEvent, Task> handler, CancellationToken cancellationToken = default)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        ThrowIfDisposed();

        if (_isSubscribed)
        {
            _logger?.LogWarning("Already subscribed to cache invalidation events");
            return;
        }

        try
        {
            _handler = handler;
            var subscriber = _connection.GetSubscriber();

            await subscriber.SubscribeAsync(
                RedisChannel.Literal(_channel),
                async (channel, message) =>
                {
                    await OnMessageReceivedAsync(message).ConfigureAwait(false);
                }).ConfigureAwait(false);

            _isSubscribed = true;

            _logger?.LogInformation(
                "Successfully subscribed to cache invalidation events on channel: {Channel}",
                _channel);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to subscribe to cache invalidation events");
            throw;
        }
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    public async Task UnsubscribeAsync()
    {
        ThrowIfDisposed();

        if (!_isSubscribed)
        {
            return;
        }

        try
        {
            var subscriber = _connection.GetSubscriber();
            await subscriber.UnsubscribeAsync(RedisChannel.Literal(_channel)).ConfigureAwait(false);

            _isSubscribed = false;
            _handler = null;

            _logger?.LogInformation(
                "Successfully unsubscribed from cache invalidation events on channel: {Channel}",
                _channel);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to unsubscribe from cache invalidation events");
            throw;
        }
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    private async Task OnMessageReceivedAsync(RedisValue message)
    {
        if (!message.HasValue || _handler == null)
        {
            return;
        }

        try
        {
            var @event = DeserializeEvent(message.ToString());
            
            if (@event != null)
            {
                _logger?.LogDebug(
                    "Received cache invalidation event for key: {Key}, Type: {Type}",
                    @event.Key,
                    @event.Type);

                await _handler(@event).ConfigureAwait(false);
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to deserialize cache invalidation event");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling cache invalidation event");
        }
    }

    /// <summary>
    /// 反序列化事件
    /// </summary>
    private CacheInvalidationEvent? DeserializeEvent(string message)
    {
        return System.Text.Json.JsonSerializer.Deserialize<CacheInvalidationEvent>(message, _jsonOptions);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RedisCacheInvalidationSubscriber));
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_isSubscribed)
            {
                // 同步取消订阅
                UnsubscribeAsync().GetAwaiter().GetResult();
            }

            _disposed = true;
        }
    }
}
