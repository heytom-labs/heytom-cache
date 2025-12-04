# 分布式锁使用指南

Heytom.Cache 提供了基于 Redis 的分布式锁实现，用于在分布式环境中实现互斥访问。

## 特性

- ✅ 基于 Redis SET NX EX 命令，符合官方推荐
- ✅ 使用 Lua 脚本确保原子性操作
- ✅ 支持锁的自动过期
- ✅ 支持锁的延长（Extend）
- ✅ 支持等待和重试机制
- ✅ 自动释放（IDisposable）
- ✅ 防止误删其他进程的锁（LockId 验证）
- ✅ 完整的日志记录

## 快速开始

### 1. 注入分布式锁工厂

```csharp
public class MyService
{
    private readonly IDistributedLockFactory _lockFactory;

    public MyService(IDistributedLockFactory lockFactory)
    {
        _lockFactory = lockFactory;
    }
}
```

### 2. 基本使用

```csharp
// 使用 using 语句自动释放锁
using var lockInstance = _lockFactory.CreateLock("my-resource");

if (await lockInstance.TryAcquireAsync(TimeSpan.FromSeconds(30)))
{
    // 执行需要互斥的操作
    await DoSomethingAsync();
}
// 锁会在 using 块结束时自动释放
```

### 3. 简化写法

```csharp
// 直接创建并获取锁
using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
    "my-resource",
    TimeSpan.FromSeconds(30)
);

if (lockInstance != null)
{
    await DoSomethingAsync();
}
```

## 核心概念

### 锁的资源名称（Resource）

资源名称是锁的唯一标识符，相同资源名称的锁会互斥。

```csharp
// 不同的资源不会互斥
var lock1 = _lockFactory.CreateLock("resource-1");
var lock2 = _lockFactory.CreateLock("resource-2");

// 相同的资源会互斥
var lock3 = _lockFactory.CreateLock("resource-1"); // 会与 lock1 互斥
```

### 锁的过期时间（Expiry）

过期时间防止死锁，即使持有锁的进程崩溃，锁也会自动释放。

```csharp
// 30 秒后自动过期
await lockInstance.TryAcquireAsync(TimeSpan.FromSeconds(30));
```

### 锁的唯一标识符（LockId）

每个锁实例都有唯一的 LockId，用于验证锁的所有权，防止误删其他进程的锁。

```csharp
var lockId = lockInstance.LockId; // 例如: "a1b2c3d4e5f6..."
```

## 高级用法

### 1. 等待和重试

```csharp
var acquired = await lockInstance.TryAcquireAsync(
    expiry: TimeSpan.FromSeconds(30),    // 锁的过期时间
    wait: TimeSpan.FromSeconds(5),       // 最多等待 5 秒
    retry: TimeSpan.FromMilliseconds(100) // 每 100ms 重试一次
);
```

### 2. 延长锁的过期时间

```csharp
using var lockInstance = _lockFactory.CreateLock("long-task");

if (await lockInstance.TryAcquireAsync(TimeSpan.FromSeconds(10)))
{
    await DoPartialWorkAsync();

    // 需要更多时间，延长锁
    if (await lockInstance.ExtendAsync(TimeSpan.FromSeconds(20)))
    {
        await DoMoreWorkAsync();
    }
}
```

### 3. 手动释放锁

```csharp
var lockInstance = _lockFactory.CreateLock("manual");

try
{
    if (await lockInstance.TryAcquireAsync(TimeSpan.FromSeconds(30)))
    {
        await DoSomethingAsync();

        // 提前释放锁
        await lockInstance.ReleaseAsync();
    }
}
finally
{
    lockInstance.Dispose();
}
```

### 4. 取消令牌支持

```csharp
var cts = new CancellationTokenSource();

try
{
    var acquired = await lockInstance.TryAcquireAsync(
        TimeSpan.FromSeconds(30),
        wait: TimeSpan.FromSeconds(10),
        cancellationToken: cts.Token
    );
}
catch (OperationCanceledException)
{
    // 操作被取消
}
```

## 常见场景

### 1. 防止重复提交（幂等性）

```csharp
public async Task<bool> SubmitOrderAsync(string orderId)
{
    var lockResource = $"order-submit:{orderId}";

    using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
        lockResource,
        TimeSpan.FromMinutes(5) // 5 分钟内不允许重复提交
    );

    if (lockInstance == null)
    {
        return false; // 订单正在处理或已提交
    }

    // 提交订单
    await ProcessOrderAsync(orderId);
    return true;
}
```

### 2. 库存扣减（防止超卖）

```csharp
public async Task<bool> DeductInventoryAsync(int productId, int quantity)
{
    var lockResource = $"inventory:{productId}";

    using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
        lockResource,
        TimeSpan.FromSeconds(10),
        wait: TimeSpan.FromSeconds(3)
    );

    if (lockInstance == null)
    {
        return false;
    }

    // 检查并扣减库存
    var stock = await GetStockAsync(productId);
    if (stock < quantity)
    {
        return false;
    }

    await UpdateStockAsync(productId, stock - quantity);
    return true;
}
```

### 3. 分布式任务调度

```csharp
public async Task ExecuteScheduledTaskAsync()
{
    var lockResource = "scheduled-task:daily-report";

    using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
        lockResource,
        TimeSpan.FromMinutes(30)
    );

    if (lockInstance == null)
    {
        // 任务已在其他实例上运行
        return;
    }

    // 执行任务
    await GenerateReportAsync();
}
```

### 4. 限流（简单令牌桶）

```csharp
public async Task<bool> CheckRateLimitAsync(string userId)
{
    var lockResource = $"rate-limit:{userId}";

    using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
        lockResource,
        TimeSpan.FromSeconds(1) // 每秒最多一次
    );

    return lockInstance != null;
}
```

### 5. 缓存更新（防止缓存击穿）

```csharp
public async Task<Product> GetProductAsync(int productId)
{
    var cacheKey = $"product:{productId}";

    // 先从缓存获取
    var cached = await _cache.GetAsync<Product>(cacheKey);
    if (cached != null)
    {
        return cached;
    }

    // 使用锁防止缓存击穿
    var lockResource = $"cache-load:{cacheKey}";

    using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
        lockResource,
        TimeSpan.FromSeconds(10),
        wait: TimeSpan.FromSeconds(3)
    );

    if (lockInstance == null)
    {
        // 其他线程正在加载，等待后重试
        await Task.Delay(100);
        return await _cache.GetAsync<Product>(cacheKey) 
            ?? await LoadFromDatabaseAsync(productId);
    }

    // 再次检查缓存（双重检查）
    cached = await _cache.GetAsync<Product>(cacheKey);
    if (cached != null)
    {
        return cached;
    }

    // 从数据库加载并缓存
    var product = await LoadFromDatabaseAsync(productId);
    await _cache.SetAsync(cacheKey, product);

    return product;
}
```

### 6. 分布式事务协调

```csharp
public async Task<bool> TransferMoneyAsync(int fromAccount, int toAccount, decimal amount)
{
    // 按账户 ID 排序，避免死锁
    var accounts = new[] { fromAccount, toAccount }.OrderBy(x => x).ToArray();

    using var lock1 = await _lockFactory.CreateLockAndAcquireAsync(
        $"account:{accounts[0]}",
        TimeSpan.FromSeconds(30),
        wait: TimeSpan.FromSeconds(5)
    );

    if (lock1 == null) return false;

    using var lock2 = await _lockFactory.CreateLockAndAcquireAsync(
        $"account:{accounts[1]}",
        TimeSpan.FromSeconds(30),
        wait: TimeSpan.FromSeconds(5)
    );

    if (lock2 == null) return false;

    // 执行转账
    await DebitAccountAsync(fromAccount, amount);
    await CreditAccountAsync(toAccount, amount);

    return true;
}
```

## 最佳实践

### 1. 合理设置过期时间

```csharp
// ❌ 不好：过期时间太短，可能导致锁提前释放
await lockInstance.TryAcquireAsync(TimeSpan.FromSeconds(1));

// ✅ 好：根据实际操作时间设置，留有余量
await lockInstance.TryAcquireAsync(TimeSpan.FromSeconds(30));
```

### 2. 使用 using 语句

```csharp
// ✅ 推荐：自动释放锁
using var lockInstance = _lockFactory.CreateLock("resource");

// ❌ 不推荐：需要手动释放
var lockInstance = _lockFactory.CreateLock("resource");
try
{
    // ...
}
finally
{
    lockInstance.Dispose();
}
```

### 3. 锁的粒度

```csharp
// ❌ 不好：锁粒度太粗
using var lockInstance = _lockFactory.CreateLock("all-products");

// ✅ 好：锁粒度细化到具体资源
using var lockInstance = _lockFactory.CreateLock($"product:{productId}");
```

### 4. 避免死锁

```csharp
// ❌ 可能死锁：不同顺序获取锁
Task1: Lock A -> Lock B
Task2: Lock B -> Lock A

// ✅ 避免死锁：按固定顺序获取锁
var sortedIds = new[] { id1, id2 }.OrderBy(x => x);
foreach (var id in sortedIds)
{
    using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync($"resource:{id}", ...);
}
```

### 5. 处理锁获取失败

```csharp
// ✅ 好：明确处理失败情况
using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(...);

if (lockInstance == null)
{
    // 记录日志
    _logger.LogWarning("Failed to acquire lock");

    // 返回错误或重试
    return Result.Failure("Resource is busy");
}
```

### 6. 长时间操作使用 Extend

```csharp
using var lockInstance = _lockFactory.CreateLock("long-task");

if (await lockInstance.TryAcquireAsync(TimeSpan.FromSeconds(30)))
{
    for (int i = 0; i < 10; i++)
    {
        await ProcessBatchAsync(i);

        // 每处理一批就延长锁
        if (i < 9)
        {
            await lockInstance.ExtendAsync(TimeSpan.FromSeconds(30));
        }
    }
}
```

## 性能考虑

### 1. 锁的开销

- 每次获取锁需要一次 Redis 网络往返（~1-5ms）
- 释放锁需要执行 Lua 脚本（~1-5ms）
- 延长锁需要执行 Lua 脚本（~1-5ms）

### 2. 优化建议

```csharp
// ❌ 不好：频繁获取释放锁
for (int i = 0; i < 1000; i++)
{
    using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(...);
    await ProcessItemAsync(i);
}

// ✅ 好：批量处理
using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(...);
for (int i = 0; i < 1000; i++)
{
    await ProcessItemAsync(i);
}
```

## 故障处理

### 1. Redis 不可用

如果 Redis 不可用，锁操作会抛出异常。建议：

```csharp
try
{
    using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(...);
    if (lockInstance != null)
    {
        await DoSomethingAsync();
    }
}
catch (RedisException ex)
{
    _logger.LogError(ex, "Redis is unavailable");
    // 降级处理
}
```

### 2. 锁过期

如果操作时间超过锁的过期时间，锁会自动释放。建议：

- 设置足够长的过期时间
- 使用 `Extend` 方法延长锁
- 检查 `IsAcquired` 属性

```csharp
if (lockInstance.IsAcquired)
{
    await DoSomethingAsync();
}
else
{
    _logger.LogWarning("Lock has expired");
}
```

## 与 Redlock 的对比

Heytom.Cache 的分布式锁是单 Redis 实例的实现，适用于大多数场景。如果需要更高的可用性，可以考虑：

- **Redlock**: 多 Redis 实例的分布式锁算法
- **Heytom.Cache**: 单 Redis 实例，简单高效

| 特性 | Heytom.Cache | Redlock |
|------|--------------|---------|
| Redis 实例数 | 1 | 3-5 |
| 复杂度 | 低 | 高 |
| 性能 | 高 | 中 |
| 可用性 | 依赖单个 Redis | 更高 |
| 适用场景 | 大多数场景 | 极高可用性要求 |

## 注意事项

1. **不要在锁内执行耗时操作** - 会阻塞其他进程
2. **合理设置过期时间** - 太短可能导致锁提前释放，太长可能导致死锁
3. **避免嵌套锁** - 可能导致死锁
4. **使用 using 语句** - 确保锁被释放
5. **处理异常** - 确保锁在异常情况下也能释放
6. **监控锁的使用** - 记录锁的获取和释放日志

## 参考资料

- [Redis 分布式锁官方文档](https://redis.io/docs/manual/patterns/distributed-locks/)
- [Redlock 算法](https://redis.io/docs/manual/patterns/distributed-locks/#the-redlock-algorithm)
