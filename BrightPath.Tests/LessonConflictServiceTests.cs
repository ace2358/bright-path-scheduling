using BrightPath.Api.Data;
using BrightPath.Api.DTOs;
using BrightPath.Api.Models;
using BrightPath.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace BrightPath.Tests;

public sealed class LessonConflictServiceTests
{
    [Fact]
    public async Task Tutor_overlap_is_rejected()
    {
        var data = await CreateTestDataAsync();
        await using var db = data.Db;
        var proposed = Proposed("T1", "R2", At(9, 30), 60, data.StudentB.Id);

        var conflicts = await new LessonConflictService(db).ValidateAsync(proposed);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(LessonConflictType.Tutor, conflict.Type);
        Assert.Equal("T1", conflict.ResourceId);
        Assert.Equal("Tutor One", conflict.ResourceName);
        Assert.Equal(data.ExistingLesson.Id, conflict.ConflictingLessonId);
    }

    [Fact]
    public async Task Room_overlap_is_rejected()
    {
        var data = await CreateTestDataAsync();
        await using var db = data.Db;
        var proposed = Proposed("T2", "R1", At(9, 30), 60, data.StudentB.Id);

        var conflicts = await new LessonConflictService(db).ValidateAsync(proposed);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(LessonConflictType.Room, conflict.Type);
        Assert.Equal("R1", conflict.ResourceId);
        Assert.Equal(data.ExistingLesson.Id, conflict.ConflictingLessonId);
    }

    [Fact]
    public async Task Student_overlap_is_rejected()
    {
        var data = await CreateTestDataAsync();
        await using var db = data.Db;
        var proposed = Proposed("T2", "R2", At(9, 30), 60, data.StudentA.Id);

        var conflicts = await new LessonConflictService(db).ValidateAsync(proposed);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(LessonConflictType.Student, conflict.Type);
        Assert.Equal(data.StudentA.Id.ToString(), conflict.ResourceId);
        Assert.Equal("Student A", conflict.ResourceName);
        Assert.Equal(data.ExistingLesson.Id, conflict.ConflictingLessonId);
    }

    [Fact]
    public async Task Multiple_conflicts_are_returned_for_the_same_overlapping_lesson()
    {
        var data = await CreateTestDataAsync();
        await using var db = data.Db;
        var proposed = Proposed("T1", "R1", At(9, 30), 60, data.StudentA.Id);

        var conflicts = await new LessonConflictService(db).ValidateAsync(proposed);

        Assert.Equal(3, conflicts.Count);
        Assert.Equal(
            [LessonConflictType.Tutor, LessonConflictType.Room, LessonConflictType.Student],
            conflicts.Select(conflict => conflict.Type));
        Assert.All(conflicts, conflict =>
            Assert.Equal(data.ExistingLesson.Id, conflict.ConflictingLessonId));
    }

    [Fact]
    public async Task Multiple_students_in_same_proposed_lesson_are_allowed()
    {
        var data = await CreateTestDataAsync();
        await using var db = data.Db;
        var proposed = Proposed("T2", "R2", At(9, 30), 60, data.StudentB.Id, data.StudentC.Id);

        var conflicts = await new LessonConflictService(db).ValidateAsync(proposed);

        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task Back_to_back_lessons_are_allowed()
    {
        var data = await CreateTestDataAsync();
        await using var db = data.Db;
        var proposed = Proposed("T1", "R1", At(10), 60, data.StudentA.Id);

        var conflicts = await new LessonConflictService(db).ValidateAsync(proposed);

        Assert.Empty(conflicts);
    }

    [Theory]
    [InlineData(LessonStatus.Cancelled)]
    [InlineData(LessonStatus.NoShow)]
    public async Task Inactive_lessons_do_not_block(LessonStatus status)
    {
        var data = await CreateTestDataAsync(status);
        await using var db = data.Db;
        var proposed = Proposed("T1", "R1", At(9, 30), 60, data.StudentA.Id);

        var conflicts = await new LessonConflictService(db).ValidateAsync(proposed);

        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task Excluding_current_lesson_prevents_self_conflict()
    {
        var data = await CreateTestDataAsync();
        await using var db = data.Db;
        var proposed = Proposed("T1", "R1", At(9), 60, data.StudentA.Id);

        var conflicts = await new LessonConflictService(db)
            .ValidateAsync(proposed, data.ExistingLesson.Id);

        Assert.Empty(conflicts);
    }

    private static ProposedLesson Proposed(
        string tutorId,
        string room,
        DateTime startAt,
        int durationMinutes,
        params int[] studentIds) =>
        new(tutorId, room, startAt, durationMinutes, studentIds);

    private static DateTime At(int hour, int minute = 0) => new(2026, 3, 4, hour, minute, 0);

    private static async Task<TestData> CreateTestDataAsync(LessonStatus status = LessonStatus.Booked)
    {
        var options = new DbContextOptionsBuilder<SchedulingDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new SchedulingDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var studentA = new Student { Name = "Student A" };
        var studentB = new Student { Name = "Student B" };
        var studentC = new Student { Name = "Student C" };
        db.AddRange(
            new Tutor { Id = "T1", Name = "Tutor One" },
            new Tutor { Id = "T2", Name = "Tutor Two" },
            studentA,
            studentB,
            studentC);
        await db.SaveChangesAsync();

        var existingLesson = new Lesson
        {
            TutorId = "T1",
            Room = "R1",
            StartAt = At(9),
            DurationMinutes = 60,
            Status = status,
            Participants =
            [
                new LessonParticipant { StudentId = studentA.Id }
            ]
        };
        db.Lessons.Add(existingLesson);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return new TestData(db, studentA, studentB, studentC, existingLesson);
    }

    private sealed record TestData(
        SchedulingDbContext Db,
        Student StudentA,
        Student StudentB,
        Student StudentC,
        Lesson ExistingLesson);
}
