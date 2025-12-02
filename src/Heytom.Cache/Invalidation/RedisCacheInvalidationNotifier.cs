using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Heytom.Cache.Invalidation;

/// <summary>
/// 基于 Redis Pub/Sub 的缓存失效通知器实现
/// </summary>
public class RedisCacheInvalidationNotifier : ICacheInvalidationNotifier
{
    private const string DefaultChannel = "heytom:cache:invalidation";
    
    private readonly IConnectionMultiplexer _connection;
    private readonly ILogger<RedisCacheInvalidationNotifier>? _logger;
    private readonly string _channel;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// 初始化 Redis 缓存失效通知器
    /// </summary>
    /// <param name="connection">Redis 连接</param>
    /// <param name="channel">发布频道（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public RedisCacheInvalidationNotifier(
        IConnectionMultiplexer connection,
        string? channel = null,
        ILogger<RedisCacheInvalidationNotifier>? logger = null)
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
    /// 发布缓存失效事件
    /// </summary>
    public async Task<bool> PublishAsync(CacheInvalidationEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event == null)
        {
            throw new ArgumentNullException(nameof(@event));
        }

        try
        {
            var subscriber = _connection.GetSubscriber();
            var message = SerializeEvent(@event);
            
            var subscriberCount = await subscriber.PublishAsync(
                RedisChannel.Literal(_channel), 
                message).ConfigureAwait(false);

            _logger?.LogDebug(
                "Published cache invalidation event for key: {Key}, Type: {Type}, Subscribers: {Count}",
                @event.Key,
                @event.Type,
                subscriberCount);

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to publish cache invalidation event for key: {Key}",
                @event.Key);
            
            // 发布失败不应影响主流程，返回 false
            return false;
        }
    }

    /// <summary>
    /// 批量发布缓存失效事件
    /// </summary>
    public async Task<int> PublishBatchAsync(IEnumerable<CacheInvalidationEvent> events, CancellationToken cancellationToken = default)
    {
        if (events == null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        var successCount = 0;
        var eventList = events.ToList();

        if (eventList.Count == 0)
        {
            return 0;
        }

        try
        {
            var subscriber = _connection.GetSubscriber();
            
            foreach (var @event in eventList)
            {
                try
                {
                    var message = SerializeEvent(@event);
                    await subscriber.PublishAsync(
                        RedisChannel.Literal(_channel), 
                        message).ConfigureAwait(false);
                    
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(
                        ex,
                        "Failed to publish cache invalidation event for key: {Key} in batch",
                        @event.Key);
                }
            }

            _logger?.LogDebug(
                "Published {SuccessCount}/{TotalCount} cache invalidation events in batch",
                successCount,
                eventList.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to publish batch cache invalidation events");
        }

        return successCount;
    }

    /// <summary>
    /// 序列化事件为 JSON 字符串
    /// </summary>
    private string SerializeEvent(CacheInvalidationEvent @event)
    {
        return System.Text.Json.JsonSerializer.Serialize(@event, _jsonOptions);
    }
}
