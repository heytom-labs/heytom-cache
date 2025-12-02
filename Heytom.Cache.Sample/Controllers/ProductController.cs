using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using System.Text;
using System.Text.Json;

namespace Heytom.Cache.Sample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductController : ControllerBase
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<ProductController> _logger;

    // Simulated database
    private static readonly List<Product> _products = new()
    {
        new Product(1, "Laptop", "High-performance laptop", 1299.99m, 50),
        new Product(2, "Mouse", "Wireless mouse", 29.99m, 200),
        new Product(3, "Keyboard", "Mechanical keyboard", 89.99m, 150),
        new Product(4, "Monitor", "27-inch 4K monitor", 399.99m, 75),
        new Product(5, "Headphones", "Noise-cancelling headphones", 249.99m, 100)
    };

    public ProductController(IDistributedCache cache, ILogger<ProductController> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get product by ID with caching (cache-aside pattern)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProduct(int id)
    {
        var cacheKey = $"product:{id}";

        // Try to get from cache first
        var cachedBytes = await _cache.GetAsync(cacheKey);
        if (cachedBytes != null)
        {
            _logger.LogInformation("Cache hit for product {ProductId}", id);
            var cachedJson = Encoding.UTF8.GetString(cachedBytes);
            var product = System.Text.Json.JsonSerializer.Deserialize<Product>(cachedJson);
            return Ok(new { source = "cache", product });
        }

        // Cache miss - get from "database"
        _logger.LogInformation("Cache miss for product {ProductId}, fetching from database", id);
        var dbProduct = _products.FirstOrDefault(p => p.Id == id);

        if (dbProduct == null)
        {
            return NotFound(new { message = "Product not found", id });
        }

        // Store in cache for future requests
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            SlidingExpiration = TimeSpan.FromMinutes(2)
        };

        var json = System.Text.Json.JsonSerializer.Serialize(dbProduct);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _cache.SetAsync(cacheKey, bytes, options);
        _logger.LogInformation("Cached product {ProductId}", id);

        return Ok(new { source = "database", product = dbProduct });
    }

    /// <summary>
    /// Get all products with caching
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllProducts()
    {
        var cacheKey = "products:all";

        var cachedBytes = await _cache.GetAsync(cacheKey);
        if (cachedBytes != null)
        {
            _logger.LogInformation("Cache hit for all products");
            var cachedJson = Encoding.UTF8.GetString(cachedBytes);
            var products = System.Text.Json.JsonSerializer.Deserialize<List<Product>>(cachedJson);
            return Ok(new { source = "cache", count = products?.Count ?? 0, products });
        }

        _logger.LogInformation("Cache miss for all products, fetching from database");

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        };

        var json = System.Text.Json.JsonSerializer.Serialize(_products);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _cache.SetAsync(cacheKey, bytes, options);

        return Ok(new { source = "database", count = _products.Count, products = _products });
    }

    /// <summary>
    /// Update product and invalidate cache (write-through pattern)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProduct(int id, [FromBody] ProductUpdateDto update)
    {
        var product = _products.FirstOrDefault(p => p.Id == id);
        if (product == null)
        {
            return NotFound(new { message = "Product not found", id });
        }

        // Update in "database"
        var updatedProduct = product with
        {
            Name = update.Name ?? product.Name,
            Description = update.Description ?? product.Description,
            Price = update.Price ?? product.Price,
            Stock = update.Stock ?? product.Stock
        };

        var index = _products.FindIndex(p => p.Id == id);
        _products[index] = updatedProduct;

        // Invalidate cache
        var cacheKey = $"product:{id}";
        await _cache.RemoveAsync(cacheKey);
        await _cache.RemoveAsync("products:all");
        _logger.LogInformation("Updated product {ProductId} and invalidated cache", id);

        return Ok(new { message = "Product updated", product = updatedProduct });
    }

    /// <summary>
    /// Delete product and invalidate cache
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = _products.FirstOrDefault(p => p.Id == id);
        if (product == null)
        {
            return NotFound(new { message = "Product not found", id });
        }

        _products.Remove(product);

        // Invalidate cache
        var cacheKey = $"product:{id}";
        await _cache.RemoveAsync(cacheKey);
        await _cache.RemoveAsync("products:all");
        _logger.LogInformation("Deleted product {ProductId} and invalidated cache", id);

        return Ok(new { message = "Product deleted", id });
    }

    /// <summary>
    /// Search products by name (demonstrating cache key patterns)
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchProducts([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { message = "Query parameter is required" });
        }

        var cacheKey = $"products:search:{query.ToLowerInvariant()}";

        var cachedBytes = await _cache.GetAsync(cacheKey);
        if (cachedBytes != null)
        {
            _logger.LogInformation("Cache hit for product search: {Query}", query);
            var cachedJson = Encoding.UTF8.GetString(cachedBytes);
            var results = System.Text.Json.JsonSerializer.Deserialize<List<Product>>(cachedJson);
            return Ok(new { source = "cache", query, count = results?.Count ?? 0, results });
        }

        _logger.LogInformation("Cache miss for product search: {Query}", query);

        var searchResults = _products
            .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       p.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3)
        };

        var json = System.Text.Json.JsonSerializer.Serialize(searchResults);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _cache.SetAsync(cacheKey, bytes, options);

        return Ok(new { source = "database", query, count = searchResults.Count, results = searchResults });
    }
}

public record Product(int Id, string Name, string Description, decimal Price, int Stock);

public record ProductUpdateDto(string? Name, string? Description, decimal? Price, int? Stock);
