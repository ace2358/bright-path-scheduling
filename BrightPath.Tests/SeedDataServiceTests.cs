using BrightPath.Api.Data;
using BrightPath.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BrightPath.Tests;

public sealed class SeedDataServiceTests
{
    [Fact]
    public async Task Exam_pair_rows_become_one_lesson_with_two_participants()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"brightpath-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dataDirectory, "tutors.csv"),
                "tutor_id,tutor_name,subject,phone\n" +
                "T1,Ngoc Anh,Maths,090xxx1122\n");
            await File.WriteAllTextAsync(
                Path.Combine(dataDirectory, "lessons_export.csv"),
                "lesson_id,date,start_time,duration_min,student,tutor_id,room,status,cancelled_at,note\n" +
                "L009,04-03-26,11:00,90,Tran Bao Long,T1,R1,booked,,exam pair - half price\n" +
                "L010,04-03-26,11:00,90,Nguyen Thi Ha,T1,R1,booked,,exam pair - half price\n");

            var options = new DbContextOptionsBuilder<SchedulingDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options;
            await using var db = new SchedulingDbContext(options);
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();

            var importer = new SeedDataService(db);
            await importer.SeedAsync(dataDirectory);
            await importer.SeedAsync(dataDirectory);

            var lesson = await db.Lessons
                .Include(item => item.Participants)
                .ThenInclude(participant => participant.Student)
                .SingleAsync();

            Assert.Equal(new DateTime(2026, 3, 4, 11, 0, 0), lesson.StartAt);
            Assert.Equal(2, lesson.Participants.Count);
            Assert.Equal(
                ["Nguyen Thi Ha", "Tran Bao Long"],
                lesson.Participants.Select(participant => participant.Student.Name).Order().ToArray());
            Assert.Equal(2, await db.Students.CountAsync());
            Assert.Equal(1, await db.Tutors.CountAsync());
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}
