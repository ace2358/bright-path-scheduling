using BrightPath.Api.Models;

namespace BrightPath.Tests;

public sealed class LessonModelTests
{
    [Fact]
    public void Lesson_can_have_multiple_students()
    {
        var lesson = new Lesson
        {
            Participants =
            [
                new LessonParticipant { Student = new Student { Name = "Student A" } },
                new LessonParticipant { Student = new Student { Name = "Student B" } }
            ]
        };

        Assert.Equal(2, lesson.Participants.Count);
    }
}
