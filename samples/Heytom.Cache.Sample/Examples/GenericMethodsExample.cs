using Heytom.Cache;
using Microsoft.Extensions.Caching.Distributed;

namespace Heytom.Cache.Sample.Examples;

/// <summary>
/// 泛型方法使用示例
/// </summary>
public class GenericMethodsExample
{
    private readonly IHybridDistributedCache _cache;

    public GenericMethodsExample(IHybridDistributedCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// 示例 1：基本的 Get/Set 操作
    /// </summary>
    public async Task BasicGetSetExample()
    {
        var product = new Product(1, "Laptop", 1299.99m);

        // 设置缓存
        await _cache.SetAsync("product:1", product, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        });

        // 获取缓存
        var cached = await _cache.GetAsync<Product>("product:1");

        Console.WriteLine($"Cached product: {cached?.Name}");
    }

    /// <summary>
    /// 示例 2：使用 GetOrSet（推荐）
    /// </summary>
    public async Task<Product> GetOrSetExample(int productId)
    {
        var key = $"product:{productId}";

        // 如果缓存存在则返回，否则执行工厂方法并缓存结果
        return await _cache.GetOrSetAsync(
            key,
            async () =>
            {
                // 模拟从数据库加载
                Console.WriteLine($"Loading product {productId} from database...");
                await Task.Delay(100); // 模拟数据库延迟
                return new Product(productId, $"Product {productId}", 99.99m);
            },
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                SlidingExpiration = TimeSpan.FromMinutes(2)
            }
        );
    }

    /// <summary>
    /// 示例 3：缓存复杂对象
    /// </summary>
    public async Task ComplexObjectExample()
    {
        var user = new User(
            Id: 1,
            Name: "John Doe",
            Email: "john@example.com",
            CreatedAt: DateTime.UtcNow,
            Roles: new[] { "Admin", "User" },
            Settings: new Dictionary<string, string>
            {
                ["Theme"] = "Dark",
                ["Language"] = "en-US"
            }
        );

        // 缓存复杂对象
        await _cache.SetAsync("user:1", user, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        });

        // 获取复杂对象
        var cachedUser = await _cache.GetAsync<User>("user:1");

        Console.WriteLine($"User: {cachedUser?.Name}, Roles: {string.Join(", ", cachedUser?.Roles ?? Array.Empty<string>())}");
    }

    /// <summary>
    /// 示例 4：列表缓存
    /// </summary>
    public async Task<List<Product>> GetProductListExample()
    {
        return await _cache.GetOrSetAsync(
            "products:all",
            async () =>
            {
                Console.WriteLine("Loading all products from database...");
                await Task.Delay(200);

                return new List<Product>
                {
                    new(1, "Laptop", 1299.99m),
                    new(2, "Mouse", 29.99m),
                    new(3, "Keyboard", 89.99m)
                };
            },
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            }
        );
    }

    /// <summary>
    /// 示例 5：搜索结果缓存
    /// </summary>
    public async Task<List<Product>> SearchProductsExample(string query)
    {
        var key = $"products:search:{query.ToLowerInvariant()}";

        return await _cache.GetOrSetAsync(
            key,
            async () =>
            {
                Console.WriteLine($"Searching products for: {query}");
                await Task.Delay(150);

                // 模拟搜索结果
                return new List<Product>
                {
                    new(1, $"Product matching {query}", 99.99m)
                };
            },
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3)
            }
        );
    }
}

// 数据模型
public record Product(int Id, string Name, decimal Price);

public record User(
    int Id,
    string Name,
    string Email,
    DateTime CreatedAt,
    string[] Roles,
    Dictionary<string, string> Settings
);
