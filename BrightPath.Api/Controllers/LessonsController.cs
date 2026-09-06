using BrightPath.Api.Data;
using BrightPath.Api.DTOs;
using BrightPath.Api.Models;
using BrightPath.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BrightPath.Api.Controllers;

[ApiController]
[Route("api/lessons")]
public sealed class LessonsController(
    SchedulingDbContext db,
    LessonConflictService conflictService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<LessonResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<LessonResponse>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();
        if (page < 1)
        {
            errors.Add(new ValidationError("page_invalid", "Page must be at least 1."));
        }

        if (pageSize is < 1 or > 100)
        {
            errors.Add(new ValidationError("page_size_invalid", "PageSize must be between 1 and 100."));
        }

        if (errors.Count > 0)
        {
            return BadRequest(new ValidationErrorResponse(errors));
        }

        var query = db.Lessons.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var offset = (long)(page - 1) * pageSize;

        IReadOnlyList<LessonResponse> lessons = [];
        if (offset < totalCount)
        {
            lessons = await query
                .OrderBy(lesson => lesson.StartAt)
                .ThenBy(lesson => lesson.Id)
                .Skip((int)offset)
                .Take(pageSize)
                .Select(lesson => new LessonResponse(
                    lesson.Id, lesson.TutorId, lesson.Tutor.Name, lesson.Room, lesson.StartAt,
                    lesson.DurationMinutes, lesson.Status, lesson.CancelledAt, lesson.Note,
                    lesson.Participants.Select(participant => participant.Student.Name).ToList()))
                .ToListAsync(cancellationToken);
        }

        return Ok(new PagedResponse<LessonResponse>(
            lessons, page, pageSize, totalCount, totalPages));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<LessonResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LessonResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var lesson = await db.Lessons.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new LessonResponse(
                item.Id, item.TutorId, item.Tutor.Name, item.Room, item.StartAt,
                item.DurationMinutes, item.Status, item.CancelledAt, item.Note,
                item.Participants.Select(participant => participant.Student.Name).ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        return lesson is null ? NotFound() : Ok(lesson);
    }

    [HttpPost]
    [ProducesResponseType<LessonResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<LessonConflictResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ValidationErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LessonResponse>> Create(
        CreateLessonRequest request,
        CancellationToken cancellationToken)
    {
        var (tutor, students, validationError) = await ResolveResourcesAsync(request, cancellationToken);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var proposedLesson = new ProposedLesson(
            request.TutorId, request.Room, request.StartAt, request.DurationMinutes,
            students.Select(student => student.Id).ToArray());
        var conflicts = await conflictService.ValidateAsync(
            proposedLesson, cancellationToken: cancellationToken);
        if (conflicts.Count > 0)
        {
            return Conflict(new LessonConflictResponse(conflicts));
        }

        var lesson = new Lesson
        {
            TutorId = tutor!.Id,
            Tutor = tutor,
            Room = request.Room,
            StartAt = request.StartAt,
            DurationMinutes = request.DurationMinutes,
            Note = request.Note,
            Participants = students.Select(student => new LessonParticipant { Student = student }).ToList()
        };
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync(cancellationToken);

        return Created($"/api/lessons/{lesson.Id}", ToResponse(lesson, students));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<LessonResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<LessonConflictResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ValidationErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LessonResponse>> Update(
        int id,
        UpdateLessonRequest request,
        CancellationToken cancellationToken)
    {
        var lesson = await db.Lessons
            .Include(existing => existing.Participants)
            .SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);
        if (lesson is null)
        {
            return NotFound();
        }

        var (tutor, students, validationError) = await ResolveResourcesAsync(request, cancellationToken);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        lesson.TutorId = tutor!.Id;
        lesson.Tutor = tutor;
        lesson.Room = request.Room;
        lesson.StartAt = request.StartAt;
        lesson.DurationMinutes = request.DurationMinutes;
        lesson.Note = request.Note;

        var proposedLesson = new ProposedLesson(
            lesson.TutorId, lesson.Room, lesson.StartAt, lesson.DurationMinutes,
            students.Select(student => student.Id).ToArray());
        var conflicts = await conflictService.ValidateAsync(proposedLesson, id, cancellationToken);
        if (conflicts.Count > 0)
        {
            return Conflict(new LessonConflictResponse(conflicts));
        }

        var requestedStudentIds = students.Select(student => student.Id).ToHashSet();
        var participantsToRemove = lesson.Participants
            .Where(participant => !requestedStudentIds.Contains(participant.StudentId))
            .ToList();
        db.LessonParticipants.RemoveRange(participantsToRemove);

        var existingStudentIds = lesson.Participants.Select(participant => participant.StudentId).ToHashSet();
        foreach (var student in students.Where(student => !existingStudentIds.Contains(student.Id)))
        {
            lesson.Participants.Add(new LessonParticipant { Lesson = lesson, Student = student });
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(lesson, students));
    }

    private async Task<(Tutor? Tutor, IReadOnlyList<Student> Students, ValidationErrorResponse? Error)>
        ResolveResourcesAsync(CreateLessonRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();
        if (string.IsNullOrWhiteSpace(request.Room))
        {
            errors.Add(new ValidationError("room_required", "Room is required."));
        }

        if (request.DurationMinutes <= 0)
        {
            errors.Add(new ValidationError("duration_invalid", "DurationMinutes must be greater than zero."));
        }

        var tutor = await db.Tutors.FindAsync([request.TutorId], cancellationToken);
        if (tutor is null)
        {
            errors.Add(new ValidationError("tutor_not_found", $"Tutor '{request.TutorId}' was not found."));
        }

        var studentIds = (request.StudentIds ?? []).Distinct().ToArray();
        var students = await db.Students
            .Where(student => studentIds.Contains(student.Id))
            .OrderBy(student => student.Id)
            .ToListAsync(cancellationToken);

        if (studentIds.Length == 0)
        {
            errors.Add(new ValidationError("students_required", "At least one student is required."));
        }
        else
        {
            var foundStudentIds = students.Select(student => student.Id).ToHashSet();
            errors.AddRange(studentIds
                .Where(studentId => !foundStudentIds.Contains(studentId))
                .Select(studentId => new ValidationError(
                    "student_not_found", $"Student '{studentId}' was not found.")));
        }

        return errors.Count == 0
            ? (tutor, students, null)
            : (tutor, students, new ValidationErrorResponse(errors));
    }

    private static LessonResponse ToResponse(Lesson lesson, IReadOnlyList<Student> students) => new(
        lesson.Id,
        lesson.TutorId,
        lesson.Tutor.Name,
        lesson.Room,
        lesson.StartAt,
        lesson.DurationMinutes,
        lesson.Status,
        lesson.CancelledAt,
        lesson.Note,
        students.Select(student => student.Name).ToList());
}
