# 分布式锁快速开始

5 分钟快速上手 Heytom.Cache 分布式锁。

## 1. 安装

分布式锁已包含在 `Heytom.Cache` 包中，无需额外安装。

```bash
dotnet add package Heytom.Cache
```

## 2. 配置

分布式锁工厂会自动注册，只需配置缓存即可：

```csharp
builder.Services.AddDistributedCache(options =>
{
    options.RedisConnectionString = "localhost:6379";
});
```

## 3. 使用

### 基本用法

```csharp
public class OrderService
{
    private readonly IDistributedLockFactory _lockFactory;

    public OrderService(IDistributedLockFactory lockFactory)
    {
        _lockFactory = lockFactory;
    }

    public async Task ProcessOrderAsync(string orderId)
    {
        // 创建并获取锁
        using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
            $"order:{orderId}",
            TimeSpan.FromMinutes(5)
        );

        if (lockInstance == null)
        {
            throw new InvalidOperationException("Order is being processed");
        }

        // 执行业务逻辑
        await DoProcessAsync(orderId);
        
        // 锁会在 using 块结束时自动释放
    }
}
```

### 常见场景

#### 1. 防止重复提交

```csharp
public async Task<bool> SubmitAsync(string id)
{
    using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
        $"submit:{id}",
        TimeSpan.FromMinutes(5)
    );

    return lockInstance != null;
}
```

#### 2. 库存扣减

```csharp
public async Task<bool> DeductStockAsync(int productId, int quantity)
{
    using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
        $"stock:{productId}",
        TimeSpan.FromSeconds(10),
        wait: TimeSpan.FromSeconds(3)
    );

    if (lockInstance == null) return false;

    var stock = await GetStockAsync(productId);
    if (stock < quantity) return false;

    await UpdateStockAsync(productId, stock - quantity);
    return true;
}
```

#### 3. 定时任务（确保单实例执行）

```csharp
public async Task RunScheduledTaskAsync()
{
    using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
        "scheduled-task:daily-report",
        TimeSpan.FromMinutes(30)
    );

    if (lockInstance == null)
    {
        // 任务已在其他实例运行
        return;
    }

    await GenerateReportAsync();
}
```

## 4. 测试

启动 Redis：

```bash
docker run -d -p 6379:6379 redis:latest
```

运行示例：

```bash
cd samples/Heytom.Cache.Sample
dotnet run
```

## 下一步

- 查看 [完整文档](DISTRIBUTED-LOCK.md)
- 查看 [示例代码](../samples/Heytom.Cache.Sample/Examples/DistributedLockExample.cs)
- 了解 [最佳实践](DISTRIBUTED-LOCK.md#最佳实践)
