using Heytom.Cache.DistributedLock;

namespace Heytom.Cache.Sample.Examples;

/// <summary>
/// 分布式锁使用示例
/// </summary>
public class DistributedLockExample
{
    private readonly IDistributedLockFactory _lockFactory;
    private readonly ILogger<DistributedLockExample> _logger;

    public DistributedLockExample(IDistributedLockFactory lockFactory, ILogger<DistributedLockExample> logger)
    {
        _lockFactory = lockFactory;
        _logger = logger;
    }

    /// <summary>
    /// 示例 1：基本的锁使用（using 语句自动释放）
    /// </summary>
    public async Task BasicLockExample()
    {
        // 使用 using 语句确保锁被释放
        using var lockInstance = _lockFactory.CreateLock("my-resource");

        if (await lockInstance.TryAcquireAsync(TimeSpan.FromSeconds(30)))
        {
            _logger.LogInformation("Lock acquired, performing critical operation...");

            // 执行需要互斥的操作
            await Task.Delay(1000);

            _logger.LogInformation("Critical operation completed");
        }
        else
        {
            _logger.LogWarning("Failed to acquire lock");
        }
        // 锁会在 using 块结束时自动释放
    }

    /// <summary>
    /// 示例 2：带等待和重试的锁
    /// </summary>
    public async Task LockWithWaitExample()
    {
        using var lockInstance = _lockFactory.CreateLock("inventory-update");

        // 尝试获取锁，最多等待 5 秒，每 100ms 重试一次
        var acquired = await lockInstance.TryAcquireAsync(
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(100)
        );

        if (acquired)
        {
            _logger.LogInformation("Lock acquired after waiting");
            await UpdateInventoryAsync();
        }
        else
        {
            _logger.LogWarning("Failed to acquire lock after 5 seconds");
        }
    }

    /// <summary>
    /// 示例 3：使用工厂方法直接获取锁
    /// </summary>
    public async Task FactoryAcquireExample()
    {
        // 直接创建并获取锁，如果失败返回 null
        using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
            "order-processing",
            TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(3)
        );

        if (lockInstance != null)
        {
            _logger.LogInformation("Lock acquired via factory");
            await ProcessOrderAsync();
        }
        else
        {
            _logger.LogWarning("Failed to acquire lock via factory");
        }
    }

    /// <summary>
    /// 示例 4：延长锁的过期时间
    /// </summary>
    public async Task ExtendLockExample()
    {
        using var lockInstance = _lockFactory.CreateLock("long-running-task");

        if (await lockInstance.TryAcquireAsync(TimeSpan.FromSeconds(10)))
        {
            _logger.LogInformation("Lock acquired for 10 seconds");

            // 执行一些操作
            await Task.Delay(5000);

            // 需要更多时间，延长锁
            if (await lockInstance.ExtendAsync(TimeSpan.FromSeconds(20)))
            {
                _logger.LogInformation("Lock extended to 20 seconds");

                // 继续执行操作
                await Task.Delay(5000);
            }
            else
            {
                _logger.LogWarning("Failed to extend lock");
            }
        }
    }

    /// <summary>
    /// 示例 5：手动释放锁
    /// </summary>
    public async Task ManualReleaseExample()
    {
        var lockInstance = _lockFactory.CreateLock("manual-release");

        try
        {
            if (await lockInstance.TryAcquireAsync(TimeSpan.FromSeconds(30)))
            {
                _logger.LogInformation("Lock acquired");

                await DoSomethingAsync();

                // 手动释放锁
                if (await lockInstance.ReleaseAsync())
                {
                    _logger.LogInformation("Lock released successfully");
                }
            }
        }
        finally
        {
            lockInstance.Dispose();
        }
    }

    /// <summary>
    /// 示例 6：防止重复提交（幂等性）
    /// </summary>
    public async Task<bool> PreventDuplicateSubmissionExample(string orderId)
    {
        var lockResource = $"order-submit:{orderId}";

        using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
            lockResource,
            TimeSpan.FromMinutes(5) // 5 分钟内不允许重复提交
        );

        if (lockInstance == null)
        {
            _logger.LogWarning("Order {OrderId} is already being processed or was recently submitted", orderId);
            return false;
        }

        _logger.LogInformation("Processing order {OrderId}", orderId);
        await SubmitOrderAsync(orderId);
        return true;
    }

    /// <summary>
    /// 示例 7：限流（令牌桶）
    /// </summary>
    public async Task<bool> RateLimitExample(string userId)
    {
        var lockResource = $"rate-limit:{userId}";

        // 尝试获取锁，不等待
        using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
            lockResource,
            TimeSpan.FromSeconds(1) // 每秒最多一次请求
        );

        if (lockInstance == null)
        {
            _logger.LogWarning("Rate limit exceeded for user {UserId}", userId);
            return false;
        }

        _logger.LogInformation("Request allowed for user {UserId}", userId);
        await ProcessRequestAsync(userId);
        return true;
    }

    /// <summary>
    /// 示例 8：分布式任务调度（确保只有一个实例执行）
    /// </summary>
    public async Task ScheduledTaskExample()
    {
        var lockResource = "scheduled-task:daily-report";

        using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
            lockResource,
            TimeSpan.FromMinutes(30) // 任务预计执行时间
        );

        if (lockInstance == null)
        {
            _logger.LogInformation("Task is already running on another instance");
            return;
        }

        _logger.LogInformation("Starting scheduled task");

        try
        {
            await GenerateDailyReportAsync();
            _logger.LogInformation("Scheduled task completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing scheduled task");
        }
    }

    /// <summary>
    /// 示例 9：库存扣减（防止超卖）
    /// </summary>
    public async Task<bool> DeductInventoryExample(int productId, int quantity)
    {
        var lockResource = $"inventory:{productId}";

        using var lockInstance = await _lockFactory.CreateLockAndAcquireAsync(
            lockResource,
            TimeSpan.FromSeconds(10),
            wait: TimeSpan.FromSeconds(3)
        );

        if (lockInstance == null)
        {
            _logger.LogWarning("Failed to acquire lock for product {ProductId}", productId);
            return false;
        }

        // 检查库存
        var currentStock = await GetInventoryAsync(productId);

        if (currentStock < quantity)
        {
            _logger.LogWarning("Insufficient stock for product {ProductId}", productId);
            return false;
        }

        // 扣减库存
        await UpdateInventoryAsync(productId, currentStock - quantity);
        _logger.LogInformation("Successfully deducted {Quantity} from product {ProductId}", quantity, productId);

        return true;
    }

    // 模拟方法
    private Task UpdateInventoryAsync() => Task.Delay(100);
    private Task ProcessOrderAsync() => Task.Delay(100);
    private Task DoSomethingAsync() => Task.Delay(100);
    private Task SubmitOrderAsync(string orderId) => Task.Delay(100);
    private Task ProcessRequestAsync(string userId) => Task.Delay(100);
    private Task GenerateDailyReportAsync() => Task.Delay(100);
    private Task<int> GetInventoryAsync(int productId) => Task.FromResult(100);
    private Task UpdateInventoryAsync(int productId, int newStock) => Task.CompletedTask;
}
