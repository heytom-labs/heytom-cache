using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using System.Text;
using System.Text.Json;

namespace Heytom.Cache.Sample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BasicCacheController : ControllerBase
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<BasicCacheController> _logger;

    public BasicCacheController(IDistributedCache cache, ILogger<BasicCacheController> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Demonstrates basic Set operation with absolute expiration
    /// </summary>
    [HttpPost("set/{key}")]
    public async Task<IActionResult> SetValue(string key, [FromBody] string value)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        var bytes = Encoding.UTF8.GetBytes(value);
        await _cache.SetAsync(key, bytes, options);
        _logger.LogInformation("Set cache key: {Key} with value: {Value}", key, value);

        return Ok(new { message = "Value cached successfully", key, value });
    }

    /// <summary>
    /// Demonstrates basic Get operation
    /// </summary>
    [HttpGet("get/{key}")]
    public async Task<IActionResult> GetValue(string key)
    {
        var bytes = await _cache.GetAsync(key);

        if (bytes == null)
        {
            _logger.LogInformation("Cache miss for key: {Key}", key);
            return NotFound(new { message = "Key not found in cache", key });
        }

        var value = Encoding.UTF8.GetString(bytes);
        _logger.LogInformation("Cache hit for key: {Key}", key);
        return Ok(new { key, value });
    }

    /// <summary>
    /// Demonstrates Remove operation
    /// </summary>
    [HttpDelete("remove/{key}")]
    public async Task<IActionResult> RemoveValue(string key)
    {
        await _cache.RemoveAsync(key);
        _logger.LogInformation("Removed cache key: {Key}", key);

        return Ok(new { message = "Value removed from cache", key });
    }

    /// <summary>
    /// Demonstrates Refresh operation for sliding expiration
    /// </summary>
    [HttpPost("refresh/{key}")]
    public async Task<IActionResult> RefreshValue(string key)
    {
        await _cache.RefreshAsync(key);
        _logger.LogInformation("Refreshed cache key: {Key}", key);

        return Ok(new { message = "Cache entry refreshed", key });
    }

    /// <summary>
    /// Demonstrates sliding expiration
    /// </summary>
    [HttpPost("set-sliding/{key}")]
    public async Task<IActionResult> SetWithSlidingExpiration(string key, [FromBody] string value)
    {
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5)
        };

        var bytes = Encoding.UTF8.GetBytes(value);
        await _cache.SetAsync(key, bytes, options);
        _logger.LogInformation("Set cache key with sliding expiration: {Key}", key);

        return Ok(new { message = "Value cached with sliding expiration", key, value });
    }

    /// <summary>
    /// Demonstrates combined expiration (absolute + sliding)
    /// </summary>
    [HttpPost("set-combined/{key}")]
    public async Task<IActionResult> SetWithCombinedExpiration(string key, [FromBody] string value)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
            SlidingExpiration = TimeSpan.FromMinutes(5)
        };

        var bytes = Encoding.UTF8.GetBytes(value);
        await _cache.SetAsync(key, bytes, options);
        _logger.LogInformation("Set cache key with combined expiration: {Key}", key);

        return Ok(new { message = "Value cached with combined expiration", key, value });
    }

    /// <summary>
    /// Demonstrates caching complex objects
    /// </summary>
    [HttpPost("set-object/{key}")]
    public async Task<IActionResult> SetObject(string key, [FromBody] UserData userData)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(userData);
        var bytes = Encoding.UTF8.GetBytes(json);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
        };

        await _cache.SetAsync(key, bytes, options);
        _logger.LogInformation("Set object in cache: {Key}", key);

        return Ok(new { message = "Object cached successfully", key, userData });
    }

    /// <summary>
    /// Demonstrates retrieving complex objects
    /// </summary>
    [HttpGet("get-object/{key}")]
    public async Task<IActionResult> GetObject(string key)
    {
        var bytes = await _cache.GetAsync(key);

        if (bytes == null)
        {
            return NotFound(new { message = "Object not found in cache", key });
        }

        var json = Encoding.UTF8.GetString(bytes);
        var userData = System.Text.Json.JsonSerializer.Deserialize<UserData>(json);
        _logger.LogInformation("Retrieved object from cache: {Key}", key);

        return Ok(new { key, userData });
    }
}

public record UserData(string Name, string Email, int Age);
