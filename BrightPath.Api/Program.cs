using BrightPath.Api.Data;
using BrightPath.Api.DTOs;
using BrightPath.Api.Models;
using BrightPath.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SchedulingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Scheduling")));
builder.Services.AddScoped<SeedDataService>();
builder.Services.AddScoped<LessonConflictService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
    await db.Database.EnsureCreatedAsync();
    var dataDirectory = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "data"));
    await scope.ServiceProvider.GetRequiredService<SeedDataService>().SeedAsync(dataDirectory);
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/api/lessons", async (SchedulingDbContext db, CancellationToken cancellationToken) =>
{
    var lessons = await db.Lessons.AsNoTracking()
        .Include(lesson => lesson.Tutor)
        .Include(lesson => lesson.Participants).ThenInclude(participant => participant.Student)
        .OrderBy(lesson => lesson.StartAt)
        .Select(lesson => new LessonResponse(
            lesson.Id, lesson.TutorId, lesson.Tutor.Name, lesson.Room, lesson.StartAt,
            lesson.DurationMinutes, lesson.Status, lesson.CancelledAt, lesson.Note,
            lesson.Participants.Select(participant => participant.Student.Name).ToList()))
        .ToListAsync(cancellationToken);
    return Results.Ok(lessons);
}).WithName("GetLessons").WithOpenApi();

app.MapPost("/api/lessons", CreateLessonAsync)
    .WithName("CreateLesson")
    .Produces<LessonResponse>(StatusCodes.Status201Created)
    .Produces<LessonConflictResponse>(StatusCodes.Status409Conflict)
    .Produces<ValidationErrorResponse>(StatusCodes.Status400BadRequest)
    .WithOpenApi();

app.MapPut("/api/lessons/{id:int}", UpdateLessonAsync)
    .WithName("UpdateLesson")
    .Produces<LessonResponse>()
    .Produces<LessonConflictResponse>(StatusCodes.Status409Conflict)
    .Produces<ValidationErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .WithOpenApi();

app.Run();

static async Task<IResult> CreateLessonAsync(
    CreateLessonRequest request,
    SchedulingDbContext db,
    LessonConflictService conflictService,
    CancellationToken cancellationToken)
{
    var (tutor, students, validationError) = await ResolveResourcesAsync(request, db, cancellationToken);
    if (validationError is not null)
    {
        return Results.BadRequest(validationError);
    }

    var proposedLesson = new ProposedLesson(
        request.TutorId, request.Room, request.StartAt, request.DurationMinutes,
        students.Select(student => student.Id).ToArray());
    var conflicts = await conflictService.ValidateAsync(proposedLesson, cancellationToken: cancellationToken);
    if (conflicts.Count > 0)
    {
        return Results.Conflict(new LessonConflictResponse(conflicts));
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

    return Results.Created($"/api/lessons/{lesson.Id}", ToResponse(lesson, students));
}

static async Task<IResult> UpdateLessonAsync(
    int id,
    UpdateLessonRequest request,
    SchedulingDbContext db,
    LessonConflictService conflictService,
    CancellationToken cancellationToken)
{
    var lesson = await db.Lessons
        .Include(existing => existing.Participants)
        .SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);
    if (lesson is null)
    {
        return Results.NotFound();
    }

    var (tutor, students, validationError) = await ResolveResourcesAsync(request, db, cancellationToken);
    if (validationError is not null)
    {
        return Results.BadRequest(validationError);
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
        return Results.Conflict(new LessonConflictResponse(conflicts));
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
    return Results.Ok(ToResponse(lesson, students));
}

static async Task<(Tutor? Tutor, IReadOnlyList<Student> Students, ValidationErrorResponse? Error)>
    ResolveResourcesAsync(
        CreateLessonRequest request,
        SchedulingDbContext db,
        CancellationToken cancellationToken)
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

static LessonResponse ToResponse(Lesson lesson, IReadOnlyList<Student> students) => new(
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

public partial class Program;
