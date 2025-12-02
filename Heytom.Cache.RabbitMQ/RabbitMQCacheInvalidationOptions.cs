namespace Heytom.Cache.RabbitMQ;

/// <summary>
/// RabbitMQ 缓存失效通知配置选项
/// </summary>
public class RabbitMQCacheInvalidationOptions
{
    /// <summary>
    /// RabbitMQ 连接字符串
    /// 格式: amqp://username:password@hostname:port/vhost
    /// </summary>
    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:5672/";

    /// <summary>
    /// Exchange 名称（使用 Fanout 类型）
    /// </summary>
    public string ExchangeName { get; set; } = "heytom.cache.invalidation";

    /// <summary>
    /// Exchange 类型
    /// </summary>
    public string ExchangeType { get; set; } = "fanout";

    /// <summary>
    /// 队列名称前缀（每个服务实例会创建唯一队列）
    /// </summary>
    public string QueueNamePrefix { get; set; } = "heytom.cache.invalidation";

    /// <summary>
    /// 是否持久化 Exchange
    /// </summary>
    public bool DurableExchange { get; set; } = true;

    /// <summary>
    /// 是否自动删除队列（服务断开后）
    /// </summary>
    public bool AutoDeleteQueue { get; set; } = true;

    /// <summary>
    /// 消息 TTL（毫秒），0 表示不过期
    /// </summary>
    public int MessageTtlMs { get; set; } = 60000; // 60 秒

    /// <summary>
    /// 连接重试次数
    /// </summary>
    public int ConnectionRetryCount { get; set; } = 3;

    /// <summary>
    /// 连接重试间隔（毫秒）
    /// </summary>
    public int ConnectionRetryDelayMs { get; set; } = 1000;
}
