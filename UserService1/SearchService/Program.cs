using Npgsql;

var builder = WebApplication.CreateBuilder(args);


// 1) Postgres connection pool (NpgsqlDataSource)

var connString = builder.Configuration.GetConnectionString("Postgres")!;
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
{
    var dsb = new NpgsqlDataSourceBuilder(connString);
    return dsb.Build(); // connection pool underneath
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
