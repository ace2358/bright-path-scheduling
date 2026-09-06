using System.Net;
using System.Net.Http.Json;
using BrightPath.Api.Data;
using BrightPath.Api.DTOs;
using BrightPath.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BrightPath.Tests;

public sealed class LessonApiTests
{
    [Fact]
    public async Task Creating_conflicting_lesson_returns_conflict_details()
    {
        using var factory = new SchedulingApplicationFactory();
        using var client = factory.CreateClient();
        var studentId = await GetStudentIdAsync(factory, "Bui An Nhien");
        var request = new CreateLessonRequest
        {
            TutorId = "T1",
            Room = "TEST-ROOM",
            StartAt = new DateTime(2026, 3, 3, 9, 30, 0),
            DurationMinutes = 30,
            StudentIds = [studentId]
        };

        var response = await client.PostAsJsonAsync("/api/lessons", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LessonConflictResponse>();
        var conflict = Assert.Single(Assert.IsType<LessonConflictResponse>(body).Conflicts);
        Assert.Equal(LessonConflictType.Tutor, conflict.Type);
        Assert.Equal("T1", conflict.ResourceId);
        Assert.True(conflict.ConflictingLessonId > 0);
    }

    [Fact]
    public async Task Creating_non_conflicting_lesson_returns_created_lesson()
    {
        using var factory = new SchedulingApplicationFactory();
        using var client = factory.CreateClient();
        var studentId = await GetStudentIdAsync(factory, "Le Minh Chau");
        var request = new CreateLessonRequest
        {
            TutorId = "T1",
            Room = "R1",
            StartAt = new DateTime(2026, 3, 11, 9, 0, 0),
            DurationMinutes = 60,
            StudentIds = [studentId],
            Note = "Swagger demonstration"
        };

        var response = await client.PostAsJsonAsync("/api/lessons", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var lesson = Assert.IsType<LessonResponse>(
            await response.Content.ReadFromJsonAsync<LessonResponse>());
        Assert.True(lesson.Id > 0);
        Assert.Equal(request.StartAt, lesson.StartAt);
        Assert.Equal(["Le Minh Chau"], lesson.Students);
        Assert.Equal($"/api/lessons/{lesson.Id}", response.Headers.Location?.OriginalString);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        Assert.True(await db.Lessons.AnyAsync(item => item.Id == lesson.Id));
    }

    [Fact]
    public async Task Updating_lesson_excludes_itself_from_conflict_detection()
    {
        using var factory = new SchedulingApplicationFactory();
        using var client = factory.CreateClient();

        int lessonId;
        int studentId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
            var lesson = await db.Lessons
                .Include(item => item.Participants)
                .SingleAsync(item =>
                    item.TutorId == "T1" &&
                    item.Room == "R1" &&
                    item.StartAt == new DateTime(2026, 3, 3, 9, 0, 0));
            lessonId = lesson.Id;
            studentId = Assert.Single(lesson.Participants).StudentId;
        }

        var request = new UpdateLessonRequest
        {
            TutorId = "T1",
            Room = "R1",
            StartAt = new DateTime(2026, 3, 3, 9, 0, 0),
            DurationMinutes = 60,
            StudentIds = [studentId],
            Note = "Updated through API"
        };

        var response = await client.PutAsJsonAsync($"/api/lessons/{lessonId}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lessonResponse = Assert.IsType<LessonResponse>(
            await response.Content.ReadFromJsonAsync<LessonResponse>());
        Assert.Equal(lessonId, lessonResponse.Id);
        Assert.Equal("Updated through API", lessonResponse.Note);
    }

    private static async Task<int> GetStudentIdAsync(
        SchedulingApplicationFactory factory,
        string studentName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        return await db.Students
            .Where(student => student.Name == studentName)
            .Select(student => student.Id)
            .SingleAsync();
    }

    private sealed class SchedulingApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string databasePath = Path.Combine(
            Path.GetTempPath(), $"brightpath-api-tests-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Scheduling"] = $"Data Source={databasePath}"
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
