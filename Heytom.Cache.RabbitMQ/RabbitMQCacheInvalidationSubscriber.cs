using System.Text;
using System.Text.Json;
using Heytom.Cache.Invalidation;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Heytom.Cache.RabbitMQ;

/// <summary>
/// 基于 RabbitMQ 的缓存失效订阅器实现
/// </summary>
public class RabbitMQCacheInvalidationSubscriber : ICacheInvalidationSubscriber
{
    private readonly RabbitMQCacheInvalidationOptions _options;
    private readonly ILogger<RabbitMQCacheInvalidationSubscriber>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private IConnection? _connection;
    private IChannel? _channel;
    private string? _queueName;
    private string? _consumerTag;
    private Func<CacheInvalidationEvent, Task>? _handler;
    private bool _isSubscribed;
    private bool _disposed;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    /// <summary>
    /// 初始化 RabbitMQ 缓存失效订阅器
    /// </summary>
    /// <param name="options">RabbitMQ 配置选项</param>
    /// <param name="logger">日志记录器（可选）</param>
    public RabbitMQCacheInvalidationSubscriber(
        RabbitMQCacheInvalidationOptions options,
        ILogger<RabbitMQCacheInvalidationSubscriber>? logger = null)
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

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _handler = handler;

            // 建立连接
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            if (_channel == null)
            {
                throw new InvalidOperationException("Failed to create RabbitMQ channel");
            }

            // 创建唯一的队列名（每个服务实例一个队列）
            _queueName = $"{_options.QueueNamePrefix}.{Guid.NewGuid():N}";

            // 声明队列
            var queueArgs = new Dictionary<string, object?>();
            if (_options.MessageTtlMs > 0)
            {
                queueArgs["x-message-ttl"] = _options.MessageTtlMs;
            }

            await _channel.QueueDeclareAsync(
                queue: _queueName,
                durable: false,
                exclusive: true, // 独占队列，连接断开自动删除
                autoDelete: _options.AutoDeleteQueue,
                arguments: queueArgs,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // 绑定队列到 Exchange
            await _channel.QueueBindAsync(
                queue: _queueName,
                exchange: _options.ExchangeName,
                routingKey: string.Empty, // Fanout 不需要 routing key
                arguments: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // 创建消费者
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += OnMessageReceivedAsync;

            // 开始消费
            _consumerTag = await _channel.BasicConsumeAsync(
                queue: _queueName,
                autoAck: true, // 自动确认
                consumer: consumer,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _isSubscribed = true;

            _logger?.LogInformation(
                "Successfully subscribed to cache invalidation events on RabbitMQ. Queue: {QueueName}, Exchange: {ExchangeName}",
                _queueName,
                _options.ExchangeName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to subscribe to cache invalidation events on RabbitMQ");
            throw;
        }
        finally
        {
            _connectionLock.Release();
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

        await _connectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_channel != null && _consumerTag != null)
            {
                try
                {
                    await _channel.BasicCancelAsync(_consumerTag).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error canceling RabbitMQ consumer");
                }
            }

            await CloseConnectionAsync().ConfigureAwait(false);

            _isSubscribed = false;
            _handler = null;
            _consumerTag = null;
            _queueName = null;

            _logger?.LogInformation("Successfully unsubscribed from cache invalidation events on RabbitMQ");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to unsubscribe from cache invalidation events on RabbitMQ");
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        if (_handler == null)
        {
            return;
        }

        try
        {
            var body = args.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var @event = DeserializeEvent(message);

            if (@event != null)
            {
                _logger?.LogDebug(
                    "Received cache invalidation event from RabbitMQ for key: {Key}, Type: {Type}",
                    @event.Key,
                    @event.Type);

                await _handler(@event).ConfigureAwait(false);
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to deserialize cache invalidation event from RabbitMQ");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling cache invalidation event from RabbitMQ");
        }
    }

    /// <summary>
    /// 确保 RabbitMQ 连接已建立
    /// </summary>
    private async Task EnsureConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection != null && _connection.IsOpen)
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

                // 声明 Exchange（确保存在）
                await _channel.ExchangeDeclareAsync(
                    exchange: _options.ExchangeName,
                    type: _options.ExchangeType,
                    durable: _options.DurableExchange,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                _logger?.LogInformation(
                    "Successfully connected to RabbitMQ for subscription. Exchange: {ExchangeName}",
                    _options.ExchangeName);

                break;
            }
            catch (Exception ex)
            {
                retryCount++;
                _logger?.LogWarning(
                    ex,
                    "Failed to connect to RabbitMQ for subscription (attempt {RetryCount}/{MaxRetries})",
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
            throw new ObjectDisposedException(nameof(RabbitMQCacheInvalidationSubscriber));
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
                UnsubscribeAsync().GetAwaiter().GetResult();
            }

            _connectionLock.Dispose();
            _disposed = true;
        }
    }
}
