using System.Text;
using System.Text.Json;
using Heytom.Cache.Invalidation;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Heytom.Cache.RabbitMQ;

/// <summary>
/// 基于 RabbitMQ 的缓存失效通知器实现
/// </summary>
public class RabbitMQCacheInvalidationNotifier : ICacheInvalidationNotifier, IDisposable
{
    private readonly RabbitMQCacheInvalidationOptions _options;
    private readonly ILogger<RabbitMQCacheInvalidationNotifier>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    /// <summary>
    /// 初始化 RabbitMQ 缓存失效通知器
    /// </summary>
    /// <param name="options">RabbitMQ 配置选项</param>
    /// <param name="logger">日志记录器（可选）</param>
    public RabbitMQCacheInvalidationNotifier(
        RabbitMQCacheInvalidationOptions options,
        ILogger<RabbitMQCacheInvalidationNotifier>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
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
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            if (_channel == null)
            {
                _logger?.LogError("RabbitMQ channel is not initialized");
                return false;
            }

            var message = SerializeEvent(@event);
            var body = Encoding.UTF8.GetBytes(message);

            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Transient, // 非持久化消息
                ContentType = "application/json",
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };

            // 如果配置了 TTL，设置消息过期时间
            if (_options.MessageTtlMs > 0)
            {
                properties.Expiration = _options.MessageTtlMs.ToString();
            }

            await _channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: string.Empty, // Fanout 不需要 routing key
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug(
                "Published cache invalidation event to RabbitMQ for key: {Key}, Type: {Type}",
                @event.Key,
                @event.Type);

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to publish cache invalidation event to RabbitMQ for key: {Key}",
                @event.Key);

            // 发布失败不应影响主流程
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
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            if (_channel == null)
            {
                _logger?.LogError("RabbitMQ channel is not initialized");
                return 0;
            }

            foreach (var @event in eventList)
            {
                try
                {
                    var success = await PublishAsync(@event, cancellationToken).ConfigureAwait(false);
                    if (success)
                    {
                        successCount++;
                    }
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
                "Published {SuccessCount}/{TotalCount} cache invalidation events to RabbitMQ in batch",
                successCount,
                eventList.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to publish batch cache invalidation events to RabbitMQ");
        }

        return successCount;
    }

    /// <summary>
    /// 确保 RabbitMQ 连接已建立
    /// </summary>
    private async Task EnsureConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection != null && _connection.IsOpen && _channel != null && _channel.IsOpen)
        {
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 双重检查
            if (_connection != null && _connection.IsOpen && _channel != null && _channel.IsOpen)
            {
                return;
            }

            // 关闭现有连接
            await CloseConnectionAsync().ConfigureAwait(false);

            // 创建新连接
            var factory = new ConnectionFactory
            {
                Uri = new Uri(_options.ConnectionString),
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                RequestedHeartbeat = TimeSpan.FromSeconds(60)
            };

            var retryCount = 0;
            while (retryCount < _options.ConnectionRetryCount)
            {
                try
                {
                    _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                    _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

                    // 声明 Exchange
                    await _channel.ExchangeDeclareAsync(
                        exchange: _options.ExchangeName,
                        type: _options.ExchangeType,
                        durable: _options.DurableExchange,
                        autoDelete: false,
                        arguments: null,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    _logger?.LogInformation(
                        "Successfully connected to RabbitMQ and declared exchange: {ExchangeName}",
                        _options.ExchangeName);

                    break;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    _logger?.LogWarning(
                        ex,
                        "Failed to connect to RabbitMQ (attempt {RetryCount}/{MaxRetries})",
                        retryCount,
                        _options.ConnectionRetryCount);

                    if (retryCount >= _options.ConnectionRetryCount)
                    {
                        throw;
                    }

                    await Task.Delay(_options.ConnectionRetryDelayMs, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// 关闭连接
    /// </summary>
    private async Task CloseConnectionAsync()
    {
        if (_channel != null)
        {
            try
            {
                await _channel.CloseAsync().ConfigureAwait(false);
                _channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error closing RabbitMQ channel");
            }
            finally
            {
                _channel = null;
            }
        }

        if (_connection != null)
        {
            try
            {
                await _connection.CloseAsync().ConfigureAwait(false);
                _connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error closing RabbitMQ connection");
            }
            finally
            {
                _connection = null;
            }
        }
    }

    /// <summary>
    /// 序列化事件为 JSON 字符串
    /// </summary>
    private string SerializeEvent(CacheInvalidationEvent @event)
    {
        return System.Text.Json.JsonSerializer.Serialize(@event, _jsonOptions);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            CloseConnectionAsync().GetAwaiter().GetResult();
            _connectionLock.Dispose();
            _disposed = true;
        }
    }
}
