using Heytom.Cache;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure distributed cache with Redis and local cache
builder.Services.AddDistributedCache(options =>
{
    options.RedisConnectionString = builder.Configuration.GetConnectionString("Redis") 
        ?? "localhost:6379";
    options.EnableLocalCache = true;
    options.LocalCacheMaxSize = 1000;
    options.LocalCacheDefaultExpiration = TimeSpan.FromMinutes(5);
    options.RedisOperationTimeout = TimeSpan.FromSeconds(5);
    options.EnableMetrics = true;
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<CacheHealthCheck>("cache");

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Map health check endpoint
app.MapHealthChecks("/health");

app.Run();
