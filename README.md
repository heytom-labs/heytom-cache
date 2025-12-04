# Heytom.Cache

一个基于 .NET 8 的高性能分布式缓存库，实现了 Microsoft 的 `IDistributedCache` 接口，并提供 Redis 扩展功能和二级本地缓存支持。

## 特性

- ✅ 完整实现 `IDistributedCache` 接口
- ✅ 二级缓存架构（本地内存缓存 + Redis）
- ✅ Redis 扩展功能（Hash、List、Set、Sorted Set、Pub/Sub）
- ✅ 强类型对象序列化支持
- ✅ 泛型类型 Get/Set 方法
- ✅ 分布式锁（基于 Redis）
- ✅ 弹性和容错机制（使用 Polly）
- ✅ 性能指标收集和监控
- ✅ OpenTelemetry 和 Prometheus 集成
- ✅ 健康检查集成
- ✅ 可配置的过期策略（绝对过期、滑动过期）
- ✅ 异步优先的 API 设计
- ✅ RabbitMQ 缓存失效通知支持

## 快速开始

### 安装

```bash
dotnet add package Heytom.Cache
```

如需 RabbitMQ 支持：

```bash
dotnet add package Heytom.Cache.RabbitMQ
```

### 基本配置

在 `Program.cs` 中配置服务：

```csharp
using Heytom.Cache;

var builder = WebApplication.CreateBuilder(args);

// 添加分布式缓存服务
builder.Services.AddDistributedCache(options =>
{
    options.RedisConnectionString = "localhost:6379";
    options.EnableLocalCache = true;
    options.LocalCacheMaxSize = 1000;
    options.LocalCacheDefaultExpiration = TimeSpan.FromMinutes(5);
    options.RedisOperationTimeout = TimeSpan.FromSeconds(5);
    options.EnableMetrics = true;
});

var app = builder.Build();
app.Run();
```

### 基本使用

#### 泛型类型操作（推荐）

```csharp
public class ProductService
{
    private readonly IHybridDistributedCache _cache;

    public ProductService(IHybridDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<Product> GetProductAsync(int id)
    {
        var key = $"product:{id}";
        
        // 使用 GetOrSet 一行代码实现 Cache-Aside 模式
        return await _cache.GetOrSetAsync(
            key,
            async () => await LoadFromDatabaseAsync(id),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            }
        );
    }
}
```

#### 标准 IDistributedCache 操作

```csharp
public class ProductService
{
    private readonly IDistributedCache _cache;

    public ProductService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        var key = $"product:{id}";
        
        // 从缓存获取（需要手动序列化）
        var bytes = await _cache.GetAsync(key);
        Product? product = null;
        
        if (bytes != null)
        {
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            product = System.Text.Json.JsonSerializer.Deserialize<Product>(json);
        }
        
        if (product == null)
        {
            // 从数据库加载
            product = await LoadFromDatabaseAsync(id);
            
            // 存入缓存，5分钟后过期
            var json = System.Text.Json.JsonSerializer.Serialize(product);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            
            await _cache.SetAsync(key, bytes, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });
        }
        
        return product;
    }
}
```

#### Redis 扩展功能

```csharp
public class CartService
{
    private readonly IRedisExtensions _redis;

    public CartService(IRedisExtensions redis)
    {
        _redis = redis;
    }

    // Hash 操作
    public async Task AddToCartAsync(string userId, string productId, int quantity)
    {
        var key = $"cart:{userId}";
        var value = JsonSerializer.SerializeToUtf8Bytes(new { productId, quantity });
        await _redis.HashSetAsync(key, productId, value);
    }

    public async Task<Dictionary<string, byte[]>> GetCartAsync(string userId)
    {
        var key = $"cart:{userId}";
        return await _redis.HashGetAllAsync(key);
    }

    // List 操作
    public async Task AddToRecentViewsAsync(string userId, string productId)
    {
        var key = $"recent:{userId}";
        var value = Encoding.UTF8.GetBytes(productId);
        await _redis.ListPushAsync(key, value);
    }

    // Pub/Sub
    public async Task NotifyPriceChangeAsync(string productId, decimal newPrice)
    {
        var message = JsonSerializer.SerializeToUtf8Bytes(new { productId, newPrice });
        await _redis.PublishAsync("price-updates", message);
    }
}
```

## 配置选项

### HybridCacheOptions

| 选项 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `RedisConnectionString` | `string` | 必填 | Redis 连接字符串 |
| `EnableLocalCache` | `bool` | `true` | 是否启用本地缓存 |
| `LocalCacheMaxSize` | `int` | `1000` | 本地缓存最大条目数 |
| `LocalCacheDefaultExpiration` | `TimeSpan` | `5分钟` | 本地缓存默认过期时间 |
| `RedisOperationTimeout` | `TimeSpan` | `5秒` | Redis 操作超时时间 |
| `EnableMetrics` | `bool` | `true` | 是否启用性能指标收集 |

### appsettings.json 配置

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379,password=yourpassword,ssl=true"
  },
  "HybridCache": {
    "EnableLocalCache": true,
    "LocalCacheMaxSize": 1000,
    "LocalCacheDefaultExpiration": "00:05:00",
    "RedisOperationTimeout": "00:00:05",
    "EnableMetrics": true
  }
}
```

然后在代码中绑定配置：

```csharp
builder.Services.AddDistributedCache(
    builder.Configuration.GetSection("HybridCache"));
```

## 高级功能

### 缓存失效通知

使用 RabbitMQ 实现跨实例的缓存失效通知：

```csharp
// 安装 Heytom.Cache.RabbitMQ 包
builder.Services.AddRabbitMQCacheInvalidation(options =>
{
    options.HostName = "localhost";
    options.Port = 5672;
    options.UserName = "guest";
    options.Password = "guest";
    options.ExchangeName = "cache-invalidation";
});
```


### 性能监控

#### 传统方式

获取缓存性能指标：

```csharp
public class MetricsController : ControllerBase
{
    private readonly MetricsCollector _metrics;

    [HttpGet("cache/metrics")]
    public ActionResult<CacheMetrics> GetMetrics()
    {
        return _metrics.GetMetrics();
    }
}
```

返回的指标包括：
- 总请求数
- 缓存命中数/未命中数
- 命中率
- 平均响应时间
- 本地缓存命中数
- Redis 命中数

#### OpenTelemetry 和 Prometheus 集成

Heytom.Cache 完全支持 OpenTelemetry 指标规范，可以轻松导出到 Prometheus、Grafana 等监控系统。

**安装 Prometheus 导出器：**

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.Prometheus.AspNetCore
```

**配置方式：**

```csharp
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("Heytom.Cache")
            .AddPrometheusExporter();  // 导出到 Prometheus
    });

app.MapPrometheusScrapingEndpoint();  // 暴露 /metrics 端点
```

**可用指标：**
- `cache_requests_total` - 缓存请求总数
- `cache_hits_total` - 缓存命中总数
- `cache_misses_total` - 缓存未命中总数
- `cache_operation_duration_milliseconds` - 操作耗时直方图

详细的 Prometheus 配置和 Grafana 查询示例，请参阅：
- [快速开始指南](docs/QUICKSTART-PROMETHEUS.md) - 5 分钟快速设置
- [完整文档](docs/PROMETHEUS.md) - 详细配置和查询示例

### 健康检查

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<CacheHealthCheck>("cache");

app.MapHealthChecks("/health");
```

健康检查会验证：
- Redis 连接状态
- 基本读写操作
- 响应时间

## 架构设计

### 二级缓存流程

```
应用请求
    ↓
本地缓存查找
    ↓
命中? → 是 → 返回数据
    ↓ 否
Redis 查找
    ↓
命中? → 是 → 更新本地缓存 → 返回数据
    ↓ 否
返回 null
```

### 容错机制

- **重试策略**: 使用 Polly 实现指数退避重试（最多 3 次）
- **断路器**: 连续失败后暂时停止访问 Redis
- **优雅降级**: Redis 不可用时继续使用本地缓存
- **超时控制**: 所有操作都有可配置的超时时间

## 性能优势

- **减少网络延迟**: 本地缓存可将响应时间从 ~5ms 降至 ~0.1ms
- **降低 Redis 负载**: 本地缓存可减少 60-80% 的 Redis 请求
- **提高可用性**: Redis 故障时仍可从本地缓存提供服务
- **LRU 驱逐**: 自动管理本地缓存大小，防止内存溢出

## 泛型类型方法

Heytom.Cache 提供了泛型扩展方法，让你可以直接操作强类型对象：

- `Get<T>` / `GetAsync<T>` - 获取强类型缓存值
- `Set<T>` / `SetAsync<T>` - 设置强类型缓存值
- `GetOrSet<T>` / `GetOrSetAsync<T>` - Cache-Aside 模式（推荐）

详细使用说明请参阅 [泛型方法文档](docs/GENERIC-METHODS.md)。

## 分布式锁

基于 Redis 的分布式锁实现，用于在分布式环境中实现互斥访问：

```csharp
public class OrderService
{
    private readonly IDistributedLockFactory _lockFactory;

    public async Task<bool> ProcessOrderAsync(string orderId)
    {
        // 使用分布式锁防止重复处理
        using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
            $"order:{orderId}",
            TimeSpan.FromMinutes(5)
        );

        if (lockInstance == null)
        {
            return false; // 订单正在处理
        }

        // 处理订单
        await DoProcessOrderAsync(orderId);
        return true;
    }
}
```

详细使用说明请参阅 [分布式锁文档](docs/DISTRIBUTED-LOCK.md)。

## 示例项目

查看 `Heytom.Cache.Sample` 项目获取完整示例：

```bash
cd Heytom.Cache.Sample
dotnet run
```

访问 Swagger UI: `https://localhost:5001/swagger`

示例包括：
- 基本缓存操作
- Redis 扩展功能演示
- 性能指标查询
- 健康检查端点

## 测试

运行单元测试：

```bash
dotnet test Heytom.Cache.Tests
```

测试覆盖：
- 基本缓存操作
- 过期策略
- 容错和弹性
- 性能指标
- 健康检查
- 服务注册

## 依赖项

- .NET 8.0
- StackExchange.Redis 2.10.1
- Microsoft.Extensions.Caching.Memory 10.0.0
- Polly 8.6.5
- RabbitMQ.Client 6.8.1 (可选)

## 许可证

本项目采用 MIT 许可证。详见 [LICENSE](LICENSE) 文件。

## 贡献

欢迎提交 Issue 和 Pull Request！

## 联系方式

如有问题或建议，请提交 Issue。
