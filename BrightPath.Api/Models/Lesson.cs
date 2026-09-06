namespace BrightPath.Api.Models;

public class Lesson
{
    public int Id { get; set; }
    public string TutorId { get; set; } = null!;
    public Tutor Tutor { get; set; } = null!;
    public string Room { get; set; } = null!;
    public DateTime StartAt { get; set; }
    public int DurationMinutes { get; set; }
    public LessonStatus Status { get; set; } = LessonStatus.Booked;
    public DateTimeOffset? CancelledAt { get; set; }
    public string? Note { get; set; }
    public ICollection<LessonParticipant> Participants { get; set; } = [];
}
