using MassTransit;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

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

// 1) Postgres connection pool (NpgsqlDataSource)

var connString = builder.Configuration.GetConnectionString("Postgres")!;

builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
{
    var dsb = new NpgsqlDataSourceBuilder(connString);
    var lf = sp.GetRequiredService<ILoggerFactory>();
    dsb.UseLoggerFactory(lf);
    return dsb.Build();
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
