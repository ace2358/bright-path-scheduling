using BrightPath.Api.Data;
using BrightPath.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SchedulingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Scheduling")));
builder.Services.AddScoped<SeedDataService>();
builder.Services.AddScoped<LessonConflictService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
    await db.Database.EnsureCreatedAsync();
    var dataDirectory = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "data"));
    await scope.ServiceProvider.GetRequiredService<SeedDataService>().SeedAsync(dataDirectory);
}

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

app.Run();

public partial class Program;
