using BrightPath.Api.Models;

namespace BrightPath.Api.DTOs;

public sealed record LessonResponse(
    int Id, string TutorId, string TutorName, string Room, DateTime StartAt,
    int DurationMinutes, LessonStatus Status, DateTimeOffset? CancelledAt,
    string? Note, IReadOnlyList<string> Students);
