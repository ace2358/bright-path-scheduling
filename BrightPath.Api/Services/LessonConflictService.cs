using BrightPath.Api.Data;
using BrightPath.Api.DTOs;
using BrightPath.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BrightPath.Api.Services;

/// <summary>Central home for new and updated lesson validation.</summary>
public sealed class LessonConflictService(SchedulingDbContext db)
{
    public async Task<IReadOnlyList<LessonConflict>> ValidateAsync(
        ProposedLesson proposedLesson, int? lessonIdToExclude = null,
        CancellationToken cancellationToken = default)
    {
        var proposedEnd = proposedLesson.StartAt.AddMinutes(proposedLesson.DurationMinutes);
        var studentIds = proposedLesson.StudentIds.Distinct().ToArray();

        var candidates = await db.Lessons
            .AsNoTracking()
            .Include(lesson => lesson.Tutor)
            .Include(lesson => lesson.Participants)
            .ThenInclude(participant => participant.Student)
            .Where(lesson =>
                lesson.Status == LessonStatus.Booked &&
                lesson.Id != lessonIdToExclude &&
                lesson.StartAt < proposedEnd &&
                (lesson.TutorId == proposedLesson.TutorId ||
                 lesson.Room == proposedLesson.Room ||
                 lesson.Participants.Any(participant => studentIds.Contains(participant.StudentId))))
            .ToListAsync(cancellationToken);

        var conflicts = new List<LessonConflict>();
        foreach (var lesson in candidates.Where(lesson =>
                     lesson.StartAt.AddMinutes(lesson.DurationMinutes) > proposedLesson.StartAt))
        {
            if (lesson.TutorId == proposedLesson.TutorId)
            {
                conflicts.Add(new LessonConflict(
                    LessonConflictType.Tutor,
                    lesson.TutorId,
                    lesson.Tutor.Name,
                    lesson.Id));
            }

            if (lesson.Room == proposedLesson.Room)
            {
                conflicts.Add(new LessonConflict(
                    LessonConflictType.Room,
                    lesson.Room,
                    lesson.Room,
                    lesson.Id));
            }

            foreach (var participant in lesson.Participants.Where(participant =>
                         studentIds.Contains(participant.StudentId)))
            {
                conflicts.Add(new LessonConflict(
                    LessonConflictType.Student,
                    participant.StudentId.ToString(),
                    participant.Student.Name,
                    lesson.Id));
            }
        }

        return conflicts;
    }
}
