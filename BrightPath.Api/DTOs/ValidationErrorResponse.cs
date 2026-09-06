namespace BrightPath.Api.DTOs;

public sealed record ValidationError(string Code, string Message);
public sealed record ValidationErrorResponse(IReadOnlyList<ValidationError> Errors);
public sealed record ProposedLesson(string TutorId, string Room, DateTime StartAt,
    int DurationMinutes, IReadOnlyList<int> StudentIds);
