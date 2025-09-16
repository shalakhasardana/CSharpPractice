using BookingService.Service;
using BookingService.Shard1;
using MassTransit;
using Npgsql;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
// Redis (localhost default)
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var cs = builder.Configuration["Redis:Connection"]!;
    var opts = ConfigurationOptions.Parse(cs);
    opts.KeepAlive = 60;
    opts.ReconnectRetryPolicy = new ExponentialRetry(5000);
    return ConnectionMultiplexer.Connect(opts);
});

builder.Services.AddScoped<IHoldService, HoldService>();

// Seats cache service
builder.Services.AddSingleton<IRedisSeatsCache, RedisSeatsCache>();

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host("localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
    });
});


// Catalog (moviedb) DataSource
var catalogConn = builder.Configuration["Catalog:ConnectionString"];
if (string.IsNullOrWhiteSpace(catalogConn))
    throw new InvalidOperationException("Catalog:ConnectionString configuration is missing or empty.");
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
    NpgsqlDataSource.Create(catalogConn));

// Shard connections
var shardConfigs = builder.Configuration.GetSection("Shards").GetChildren()
    .Select(s => (Id: int.Parse(s["Id"]!), Conn: s["ConnectionString"]!))
    .ToList();


builder.Services.AddSingleton<IShardDb>(new ShardDb(shardConfigs));

builder.Services.AddSingleton<IShardResolver>(sp =>
{
    var catalog = sp.GetRequiredService<NpgsqlDataSource>();
    // If no mapping is found in the catalog, fallback to jump hash across these shards
    var fallbackShards = shardConfigs.Count;
    return new CatalogFirstShardResolver(catalog, fallbackShards);
});


// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
