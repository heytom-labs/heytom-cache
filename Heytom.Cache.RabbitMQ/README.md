# Heytom.Cache.RabbitMQ

基于 RabbitMQ 的分布式缓存失效通知实现，用于 Heytom.Cache 混合缓存系统。

## 功能特性

- ✅ 基于 RabbitMQ Fanout Exchange 的发布/订阅模式
- ✅ 自动重连和故障恢复
- ✅ 支持消息 TTL 配置
- ✅ 每个服务实例独立队列（自动清理）
- ✅ 完整的日志记录
- ✅ 线程安全
- ✅ 实现 `ICacheInvalidationNotifier` 和 `ICacheInvalidationSubscriber` 接口

## 安装

```bash
dotnet add package Heytom.Cache.RabbitMQ
```

## 快速开始

### 1. 基本配置

```csharp
using Heytom.Cache;
using Heytom.Cache.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// 配置混合缓存（使用 RabbitMQ 进行失效通知）
builder.Services.AddDistributedCache(options =>
{
    options.RedisConnectionString = "localhost:6379";
    options.EnableLocalCache = true;
    options.EnableCacheInvalidation = true;
});

// 添加 RabbitMQ 缓存失效通知
builder.Services.AddRabbitMQCacheInvalidation(options =>
{
    options.ConnectionString = "amqp://guest:guest@localhost:5672/";
    options.ExchangeName = "heytom.cache.invalidation";
});

var app = builder.Build();
app.Run();
```

### 2. 从配置文件读取

**appsettings.json:**
```json
{
  "HybridCache": {
    "RedisConnectionString": "localhost:6379",
    "EnableLocalCache": true,
    "EnableCacheInvalidation": true
  },
  "RabbitMQCacheInvalidation": {
    "ConnectionString": "amqp://guest:guest@localhost:5672/",
    "ExchangeName": "heytom.cache.invalidation",
    "MessageTtlMs": 60000,
    "ConnectionRetryCount": 3
  }
}
```

**Program.cs:**
```csharp
builder.Services.AddDistributedCache(
    builder.Configuration.GetSection("HybridCache"));

builder.Services.AddRabbitMQCacheInvalidation(
    builder.Configuration.GetSection("RabbitMQCacheInvalidation"));
```

### 3. 简化配置

```csharp
// 使用连接字符串快速配置
builder.Services.AddRabbitMQCacheInvalidation(
    connectionString: "amqp://guest:guest@localhost:5672/",
    exchangeName: "my.cache.invalidation");
```

## 配置选项

| 选项 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `ConnectionString` | string | `amqp://guest:guest@localhost:5672/` | RabbitMQ 连接字符串 |
| `ExchangeName` | string | `heytom.cache.invalidation` | Exchange 名称 |
| `ExchangeType` | string | `fanout` | Exchange 类型 |
| `QueueNamePrefix` | string | `heytom.cache.invalidation` | 队列名称前缀 |
| `DurableExchange` | bool | `true` | 是否持久化 Exchange |
| `AutoDeleteQueue` | bool | `true` | 是否自动删除队列 |
| `MessageTtlMs` | int | `60000` | 消息 TTL（毫秒） |
| `ConnectionRetryCount` | int | `3` | 连接重试次数 |
| `ConnectionRetryDelayMs` | int | `1000` | 连接重试间隔（毫秒） |

## 工作原理

### 架构图

```
┌─────────────┐         ┌─────────────┐         ┌─────────────┐
│  Service A  │         │  Service B  │         │  Service C  │
│             │         │             │         │             │
│  ┌───────┐  │         │  ┌───────┐  │         │  ┌───────┐  │
│  │ Local │  │         │  │ Local │  │         │  │ Local │  │
│  │ Cache │  │         │  │ Cache │  │         │  │ Cache │  │
│  └───┬───┘  │         │  └───┬───┘  │         │  └───┬───┘  │
│      │      │         │      │      │         │      │      │
│  ┌───▼───┐  │         │  ┌───▼───┐  │         │  ┌───▼───┐  │
│  │ Redis │  │         │  │ Redis │  │         │  │ Redis │  │
│  └───┬───┘  │         │  └───┬───┘  │         │  └───┬───┘  │
│      │      │         │      │      │         │      │      │
│  ┌───▼───┐  │         │  ┌───▼───┐  │         │  ┌───▼───┐  │
│  │  MQ   │  │         │  │  MQ   │  │         │  │  MQ   │  │
│  │Publish│  │         │  │  Sub  │  │         │  │  Sub  │  │
│  └───┬───┘  │         │  └───▲───┘  │         │  └───▲───┘  │
└──────┼──────┘         └──────┼──────┘         └──────┼──────┘
       │                       │                       │
       │    ┌──────────────────┴───────────────────────┘
       │    │
       ▼    ▼
   ┌────────────────┐
   │   RabbitMQ     │
   │   Exchange     │
   │   (Fanout)     │
   └────────────────┘
```

### 流程说明

1. **服务 A 更新缓存**：
   - 更新 Redis 和本地缓存
   - 发布失效事件到 RabbitMQ Exchange

2. **RabbitMQ 广播**：
   - Fanout Exchange 将消息广播到所有绑定的队列
   - 每个服务实例有自己的独立队列

3. **其他服务接收**：
   - 服务 B 和 C 接收失效消息
   - 清除各自的本地缓存
   - Redis 数据保持一致

## 与 Redis Pub/Sub 对比

| 特性 | RabbitMQ | Redis Pub/Sub |
|------|----------|---------------|
| 消息可靠性 | ✅ 高（队列持久化） | ❌ 低（订阅者离线丢失） |
| 消息顺序 | ✅ 保证 | ✅ 保证 |
| 持久化 | ✅ 支持 | ❌ 不支持 |
| 消息 TTL | ✅ 支持 | ❌ 不支持 |
| 性能 | 🟡 中等 | ✅ 高 |
| 复杂度 | 🟡 需要额外服务 | ✅ 无需额外服务 |
| 适用场景 | 高可靠性要求 | 高性能要求 |

## 最佳实践

### 1. 生产环境配置

```csharp
builder.Services.AddRabbitMQCacheInvalidation(options =>
{
    options.ConnectionString = "amqp://user:pass@rabbitmq-cluster:5672/";
    options.ExchangeName = "prod.cache.invalidation";
    options.DurableExchange = true;
    options.MessageTtlMs = 30000; // 30 秒
    options.ConnectionRetryCount = 5;
    options.ConnectionRetryDelayMs = 2000;
});
```

### 2. 监控和日志

```csharp
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});
```

### 3. 健康检查

```csharp
builder.Services.AddHealthChecks()
    .AddCacheHealthCheck(); // 包含 RabbitMQ 连接状态
```

## 故障处理

### 连接失败

- 自动重试机制（可配置重试次数和间隔）
- 连接失败不影响主缓存功能
- 详细的错误日志记录

### 消息丢失

- 使用持久化 Exchange
- 配置合理的消息 TTL
- 本地缓存有兜底的过期时间

### 性能优化

- 使用 Fanout Exchange（最快的路由方式）
- 非持久化消息（减少磁盘 I/O）
- 自动确认消息（提高吞吐量）

## 示例代码

### 完整示例

```csharp
using Heytom.Cache;
using Heytom.Cache.RabbitMQ;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);

// 配置缓存
builder.Services.AddDistributedCache(options =>
{
    options.RedisConnectionString = "localhost:6379";
    options.EnableLocalCache = true;
    options.LocalCacheMaxSize = 1000;
    options.LocalCacheDefaultExpiration = TimeSpan.FromMinutes(5);
    options.EnableCacheInvalidation = true;
});

// 配置 RabbitMQ 失效通知
builder.Services.AddRabbitMQCacheInvalidation(options =>
{
    options.ConnectionString = "amqp://guest:guest@localhost:5672/";
    options.ExchangeName = "cache.invalidation";
    options.MessageTtlMs = 60000;
});

var app = builder.Build();

// 使用缓存
app.MapGet("/set/{key}/{value}", async (
    string key,
    string value,
    IDistributedCache cache) =>
{
    await cache.SetStringAsync(key, value);
    return Results.Ok($"Set {key} = {value}");
});

app.MapGet("/get/{key}", async (
    string key,
    IDistributedCache cache) =>
{
    var value = await cache.GetStringAsync(key);
    return Results.Ok(new { key, value });
});

app.Run();
```

## 许可证

MIT License

## 相关项目

- [Heytom.Cache](../Heytom.Cache) - 核心缓存库
- [RabbitMQ.Client](https://www.rabbitmq.com/dotnet.html) - RabbitMQ .NET 客户端
