namespace BrightPath.Api.Models;

public class LessonParticipant
{
    public int LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
}
