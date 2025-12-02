using Microsoft.AspNetCore.Mvc;
using Heytom.Cache;

namespace Heytom.Cache.Sample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly MetricsCollector _metrics;
    private readonly ILogger<MetricsController> _logger;

    public MetricsController(MetricsCollector metrics, ILogger<MetricsController> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Get current cache metrics
    /// </summary>
    [HttpGet]
    public IActionResult GetMetrics()
    {
        var metrics = _metrics.GetMetrics();
        _logger.LogInformation("Retrieved cache metrics: Hit rate: {HitRate:P2}", metrics.HitRate);

        return Ok(new
        {
            totalRequests = metrics.TotalRequests,
            cacheHits = metrics.CacheHits,
            cacheMisses = metrics.CacheMisses,
            hitRate = $"{metrics.HitRate:P2}",
            averageResponseTimeMs = metrics.AverageResponseTimeMs,
            localCacheHits = metrics.LocalCacheHits,
            redisHits = metrics.RedisHits
        });
    }

    /// <summary>
    /// Reset cache metrics
    /// </summary>
    [HttpPost("reset")]
    public IActionResult ResetMetrics()
    {
        _metrics.Reset();
        _logger.LogInformation("Cache metrics reset");

        return Ok(new { message = "Metrics reset successfully" });
    }
}
