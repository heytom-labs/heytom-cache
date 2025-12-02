using Microsoft.AspNetCore.Mvc;
using Heytom.Cache;
using System.Text;

namespace Heytom.Cache.Sample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RedisExtensionsController : ControllerBase
{
    private readonly IRedisExtensions _redis;
    private readonly ILogger<RedisExtensionsController> _logger;

    public RedisExtensionsController(IRedisExtensions redis, ILogger<RedisExtensionsController> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    #region Hash Operations

    /// <summary>
    /// Set a field in a Redis hash
    /// </summary>
    [HttpPost("hash/{key}/{field}")]
    public async Task<IActionResult> HashSet(string key, string field, [FromBody] string value)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var result = await _redis.HashSetAsync(key, field, valueBytes);
        _logger.LogInformation("HashSet: {Key}.{Field} = {Value}", key, field, value);

        return Ok(new { message = "Hash field set", key, field, value, isNew = result });
    }

    /// <summary>
    /// Get a field from a Redis hash
    /// </summary>
    [HttpGet("hash/{key}/{field}")]
    public async Task<IActionResult> HashGet(string key, string field)
    {
        var valueBytes = await _redis.HashGetAsync(key, field);

        if (valueBytes == null)
        {
            return NotFound(new { message = "Hash field not found", key, field });
        }

        var value = Encoding.UTF8.GetString(valueBytes);
        _logger.LogInformation("HashGet: {Key}.{Field} = {Value}", key, field, value);

        return Ok(new { key, field, value });
    }

    /// <summary>
    /// Get all fields from a Redis hash
    /// </summary>
    [HttpGet("hash/{key}")]
    public async Task<IActionResult> HashGetAll(string key)
    {
        var hash = await _redis.HashGetAllAsync(key);
        var result = hash.ToDictionary(
            kvp => kvp.Key,
            kvp => Encoding.UTF8.GetString(kvp.Value)
        );

        _logger.LogInformation("HashGetAll: {Key} returned {Count} fields", key, result.Count);

        return Ok(new { key, fields = result });
    }

    /// <summary>
    /// Delete a field from a Redis hash
    /// </summary>
    [HttpDelete("hash/{key}/{field}")]
    public async Task<IActionResult> HashDelete(string key, string field)
    {
        var result = await _redis.HashDeleteAsync(key, field);
        _logger.LogInformation("HashDelete: {Key}.{Field}, existed: {Existed}", key, field, result);

        return Ok(new { message = "Hash field deleted", key, field, existed = result });
    }

    #endregion

    #region List Operations

    /// <summary>
    /// Push a value to a Redis list
    /// </summary>
    [HttpPost("list/{key}")]
    public async Task<IActionResult> ListPush(string key, [FromBody] string value)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var length = await _redis.ListPushAsync(key, valueBytes);
        _logger.LogInformation("ListPush: {Key}, new length: {Length}", key, length);

        return Ok(new { message = "Value pushed to list", key, value, length });
    }

    /// <summary>
    /// Pop a value from a Redis list
    /// </summary>
    [HttpPost("list/{key}/pop")]
    public async Task<IActionResult> ListPop(string key)
    {
        var valueBytes = await _redis.ListPopAsync(key);

        if (valueBytes == null)
        {
            return NotFound(new { message = "List is empty or does not exist", key });
        }

        var value = Encoding.UTF8.GetString(valueBytes);
        _logger.LogInformation("ListPop: {Key} = {Value}", key, value);

        return Ok(new { key, value });
    }

    /// <summary>
    /// Get the length of a Redis list
    /// </summary>
    [HttpGet("list/{key}/length")]
    public async Task<IActionResult> ListLength(string key)
    {
        var length = await _redis.ListLengthAsync(key);
        _logger.LogInformation("ListLength: {Key} = {Length}", key, length);

        return Ok(new { key, length });
    }

    #endregion

    #region Set Operations

    /// <summary>
    /// Add a value to a Redis set
    /// </summary>
    [HttpPost("set/{key}")]
    public async Task<IActionResult> SetAdd(string key, [FromBody] string value)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var added = await _redis.SetAddAsync(key, valueBytes);
        _logger.LogInformation("SetAdd: {Key}, value: {Value}, added: {Added}", key, value, added);

        return Ok(new { message = "Value added to set", key, value, added });
    }

    /// <summary>
    /// Remove a value from a Redis set
    /// </summary>
    [HttpDelete("set/{key}")]
    public async Task<IActionResult> SetRemove(string key, [FromBody] string value)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var removed = await _redis.SetRemoveAsync(key, valueBytes);
        _logger.LogInformation("SetRemove: {Key}, value: {Value}, removed: {Removed}", key, value, removed);

        return Ok(new { message = "Value removed from set", key, value, removed });
    }

    /// <summary>
    /// Get all members of a Redis set
    /// </summary>
    [HttpGet("set/{key}")]
    public async Task<IActionResult> SetMembers(string key)
    {
        var membersBytes = await _redis.SetMembersAsync(key);
        var members = membersBytes.Select(b => Encoding.UTF8.GetString(b)).ToList();
        _logger.LogInformation("SetMembers: {Key} returned {Count} members", key, members.Count);

        return Ok(new { key, members });
    }

    #endregion

    #region Sorted Set Operations

    /// <summary>
    /// Add a value with score to a Redis sorted set
    /// </summary>
    [HttpPost("sortedset/{key}")]
    public async Task<IActionResult> SortedSetAdd(string key, [FromQuery] double score, [FromBody] string value)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var added = await _redis.SortedSetAddAsync(key, valueBytes, score);
        _logger.LogInformation("SortedSetAdd: {Key}, value: {Value}, score: {Score}, added: {Added}", 
            key, value, score, added);

        return Ok(new { message = "Value added to sorted set", key, value, score, added });
    }

    /// <summary>
    /// Get values from a Redis sorted set by score range
    /// </summary>
    [HttpGet("sortedset/{key}")]
    public async Task<IActionResult> SortedSetRangeByScore(string key, [FromQuery] double min, [FromQuery] double max)
    {
        var valuesBytes = await _redis.SortedSetRangeByScoreAsync(key, min, max);
        var values = valuesBytes.Select(b => Encoding.UTF8.GetString(b)).ToList();
        _logger.LogInformation("SortedSetRangeByScore: {Key} [{Min}, {Max}] returned {Count} values", 
            key, min, max, values.Count);

        return Ok(new { key, min, max, values });
    }

    #endregion

    #region Pub/Sub Operations

    /// <summary>
    /// Publish a message to a Redis channel
    /// </summary>
    [HttpPost("pubsub/publish/{channel}")]
    public async Task<IActionResult> Publish(string channel, [FromBody] string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        await _redis.PublishAsync(channel, messageBytes);
        _logger.LogInformation("Published to channel: {Channel}, message: {Message}", channel, message);

        return Ok(new { message = "Message published", channel, content = message });
    }

    /// <summary>
    /// Subscribe to a Redis channel (for demonstration - in real apps use SignalR or similar)
    /// </summary>
    [HttpPost("pubsub/subscribe/{channel}")]
    public async Task<IActionResult> Subscribe(string channel)
    {
        var messages = new List<string>();
        
        await _redis.SubscribeAsync(channel, messageBytes =>
        {
            var message = Encoding.UTF8.GetString(messageBytes);
            messages.Add(message);
            _logger.LogInformation("Received message on channel {Channel}: {Message}", channel, message);
        });

        return Ok(new { message = "Subscribed to channel", channel });
    }

    #endregion
}
