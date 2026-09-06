using BrightPath.Api.Data;
using BrightPath.Api.DTOs;

namespace BrightPath.Api.Services;

/// <summary>Central home for new and updated lesson validation.</summary>
public sealed class LessonConflictService(SchedulingDbContext db)
{
    public Task<IReadOnlyList<ValidationError>> ValidateAsync(
        ProposedLesson proposedLesson, int? lessonIdToExclude = null,
        CancellationToken cancellationToken = default)
    {
        _ = db;
        _ = proposedLesson;
        _ = lessonIdToExclude;
        _ = cancellationToken;
        return Task.FromResult<IReadOnlyList<ValidationError>>([]);
    }
}
