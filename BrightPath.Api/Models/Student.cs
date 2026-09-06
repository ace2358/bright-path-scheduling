namespace BrightPath.Api.Models;

public class Student
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public ICollection<LessonParticipant> LessonParticipants { get; set; } = [];
}
