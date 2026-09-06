namespace BrightPath.Api.DTOs;

public enum LessonConflictType
{
    Tutor,
    Room,
    Student
}

public sealed record LessonConflict(
    LessonConflictType Type,
    string ResourceId,
    string ResourceName,
    int ConflictingLessonId);
