using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// DB
var cs = builder.Configuration.GetConnectionString("Postgres")!;
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
{
    var b = new NpgsqlDataSourceBuilder(cs);
    return b.Build();
});

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// AuthN
var jwt = builder.Configuration.GetSection("Jwt");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = key
        };
    });

// AuthZ (permission policies)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("perm:Theater_Read", p => p.RequireClaim("perm", "Theater_Read"));
    options.AddPolicy("perm:Theater_Write", p => p.RequireClaim("perm", "Theater_Write"));
    options.AddPolicy("perm:Create_Show", p => p.RequireClaim("perm", "Create_Show"));
    options.AddPolicy("perm:Update_Show", p => p.RequireClaim("perm", "Update_Show"));
    options.AddPolicy("perm:Cancel_Show", p => p.RequireClaim("perm", "Cancel_Show"));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
