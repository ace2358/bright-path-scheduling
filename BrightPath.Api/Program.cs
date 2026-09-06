using BrightPath.Api.Data;
using BrightPath.Api.DTOs;
using BrightPath.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SchedulingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Scheduling")));
builder.Services.AddScoped<SeedDataService>();
builder.Services.AddScoped<LessonConflictService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
    await db.Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<SeedDataService>().SeedAsync();
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/api/lessons", async (SchedulingDbContext db, CancellationToken cancellationToken) =>
{
    var lessons = await db.Lessons.AsNoTracking()
        .Include(lesson => lesson.Tutor)
        .Include(lesson => lesson.Participants).ThenInclude(participant => participant.Student)
        .OrderBy(lesson => lesson.StartAt)
        .Select(lesson => new LessonResponse(
            lesson.Id, lesson.TutorId, lesson.Tutor.Name, lesson.Room, lesson.StartAt,
            lesson.DurationMinutes, lesson.Status, lesson.CancelledAt, lesson.Note,
            lesson.Participants.Select(participant => participant.Student.Name).ToList()))
        .ToListAsync(cancellationToken);
    return Results.Ok(lessons);
}).WithName("GetLessons").WithOpenApi();

app.MapPost("/api/lessons", (CreateLessonRequest _) =>
    Results.StatusCode(StatusCodes.Status501NotImplemented))
    .WithName("CreateLesson").WithOpenApi();

app.MapPut("/api/lessons/{id:int}", (int _, UpdateLessonRequest __) =>
    Results.StatusCode(StatusCodes.Status501NotImplemented))
    .WithName("UpdateLesson").WithOpenApi();

app.Run();

public partial class Program;
