namespace BrightPath.Api.DTOs;

public class CreateLessonRequest
{
    public string TutorId { get; init; } = string.Empty;
    public string Room { get; init; } = string.Empty;
    public DateTime StartAt { get; init; }
    public int DurationMinutes { get; init; }
    public IReadOnlyList<int> StudentIds { get; init; } = [];
    public string? Note { get; init; }
}
