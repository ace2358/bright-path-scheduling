using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task Tutors_are_returned_in_id_order_with_only_id_and_name()
    {
        using var factory = new SchedulingApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/tutors");

        response.EnsureSuccessStatusCode();
        var tutors = Assert.IsType<List<TutorResponse>>(
            await response.Content.ReadFromJsonAsync<List<TutorResponse>>());
        Assert.Equal(["T1", "T2", "T3"], tutors.Select(tutor => tutor.Id));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.All(json.RootElement.EnumerateArray(), tutor =>
            Assert.Equal(["id", "name"], tutor.EnumerateObject().Select(property => property.Name)));
    }

    [Fact]
    public async Task Students_are_returned_in_id_order_with_only_id_and_name()
    {
        using var factory = new SchedulingApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/students");

        response.EnsureSuccessStatusCode();
        var students = Assert.IsType<List<StudentResponse>>(
            await response.Content.ReadFromJsonAsync<List<StudentResponse>>());
        Assert.Equal(students.OrderBy(student => student.Id).Select(student => student.Id),
            students.Select(student => student.Id));
        Assert.Equal(6, students.Count);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.All(json.RootElement.EnumerateArray(), student =>
            Assert.Equal(["id", "name"], student.EnumerateObject().Select(property => property.Name)));
    }

    [Fact]
    public async Task Lessons_are_paginated_with_defaults_and_stable_ordering()
    {
        using var factory = new SchedulingApplicationFactory();
        using var client = factory.CreateClient();

        var defaultPage = Assert.IsType<PagedResponse<LessonResponse>>(
            await client.GetFromJsonAsync<PagedResponse<LessonResponse>>("/api/lessons"));
        var secondPage = Assert.IsType<PagedResponse<LessonResponse>>(
            await client.GetFromJsonAsync<PagedResponse<LessonResponse>>("/api/lessons?page=2&pageSize=5"));

        Assert.Equal(1, defaultPage.Page);
        Assert.Equal(20, defaultPage.PageSize);
        Assert.Equal(33, defaultPage.TotalCount);
        Assert.Equal(2, defaultPage.TotalPages);
        Assert.Equal(20, defaultPage.Items.Count);

        Assert.Equal(2, secondPage.Page);
        Assert.Equal(5, secondPage.PageSize);
        Assert.Equal(33, secondPage.TotalCount);
        Assert.Equal(7, secondPage.TotalPages);
        Assert.Equal(5, secondPage.Items.Count);
        Assert.Equal(
            secondPage.Items.OrderBy(lesson => lesson.StartAt).ThenBy(lesson => lesson.Id).Select(lesson => lesson.Id),
            secondPage.Items.Select(lesson => lesson.Id));
    }

    [Fact]
    public async Task Invalid_pagination_returns_bad_request()
    {
        using var factory = new SchedulingApplicationFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[]
                 {
                     "/api/lessons?page=0",
                     "/api/lessons?pageSize=0",
                     "/api/lessons?pageSize=101"
                 })
        {
            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = Assert.IsType<ValidationErrorResponse>(
                await response.Content.ReadFromJsonAsync<ValidationErrorResponse>());
            Assert.NotEmpty(body.Errors);
        }
    }

    [Fact]
    public async Task Getting_existing_lesson_by_id_returns_lesson_response()
    {
        using var factory = new SchedulingApplicationFactory();
        using var client = factory.CreateClient();

        int lessonId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
            lessonId = await db.Lessons
                .Where(lesson => lesson.TutorId == "T1" && lesson.Room == "R1")
                .OrderBy(lesson => lesson.Id)
                .Select(lesson => lesson.Id)
                .FirstAsync();
        }

        var response = await client.GetAsync($"/api/lessons/{lessonId}");

        response.EnsureSuccessStatusCode();
        var lesson = Assert.IsType<LessonResponse>(
            await response.Content.ReadFromJsonAsync<LessonResponse>());
        Assert.Equal(lessonId, lesson.Id);
        Assert.Equal("T1", lesson.TutorId);
        Assert.Equal("Ngoc Anh", lesson.TutorName);
        Assert.NotEmpty(lesson.Students);
    }

    [Fact]
    public async Task Getting_unknown_lesson_by_id_returns_not_found()
    {
        using var factory = new SchedulingApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/lessons/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

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
