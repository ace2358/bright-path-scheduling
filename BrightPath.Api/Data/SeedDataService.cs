using System.Globalization;
using System.Text;
using BrightPath.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BrightPath.Api.Data;

/// <summary>
/// Imports the provided historical CSV exports without applying scheduling validation.
/// </summary>
public sealed class SeedDataService(SchedulingDbContext db)
{
    private const string SharedLessonNote = "exam pair - half price";
    private static readonly string[] SupportedDateFormats = ["dd-MM-yy", "yyyy-MM-dd"];

    public async Task SeedAsync(string dataDirectory, CancellationToken cancellationToken = default)
    {
        if (await db.Lessons.AnyAsync(cancellationToken))
        {
            return;
        }

        var tutorsPath = Path.Combine(dataDirectory, "tutors.csv");
        var lessonsPath = Path.Combine(dataDirectory, "lessons_export.csv");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var tutors = await ImportTutorsAsync(tutorsPath, cancellationToken);
        var students = await db.Students.ToDictionaryAsync(
            student => student.Name,
            StringComparer.OrdinalIgnoreCase,
            cancellationToken);
        var sharedLessons = new Dictionary<SharedLessonKey, Lesson>();

        await foreach (var row in ReadCsvAsync(lessonsPath, cancellationToken))
        {
            var sourceId = Required(row, "lesson_id", lessonsPath);
            var tutorId = Required(row, "tutor_id", lessonsPath);
            if (!tutors.ContainsKey(tutorId))
            {
                throw new InvalidDataException($"Lesson '{sourceId}' references unknown tutor '{tutorId}'.");
            }

            var studentName = Required(row, "student", lessonsPath);
            if (!students.TryGetValue(studentName, out var student))
            {
                student = new Student { Name = studentName };
                students.Add(studentName, student);
                db.Students.Add(student);
            }

            var date = ParseDate(Required(row, "date", lessonsPath), sourceId);
            var time = ParseTime(Required(row, "start_time", lessonsPath), sourceId);
            var duration = ParsePositiveInteger(Required(row, "duration_min", lessonsPath), sourceId);
            var room = Required(row, "room", lessonsPath);
            var status = ParseStatus(Required(row, "status", lessonsPath), sourceId);
            var note = Optional(row, "note");
            var cancelledAt = ParseCancelledAt(Optional(row, "cancelled_at"), sourceId);
            var startAt = date.ToDateTime(time, DateTimeKind.Unspecified);

            Lesson lesson;
            if (note?.Contains(SharedLessonNote, StringComparison.OrdinalIgnoreCase) == true)
            {
                var key = new SharedLessonKey(startAt, duration, tutorId, room, status, note);
                if (!sharedLessons.TryGetValue(key, out lesson!))
                {
                    lesson = CreateLesson(tutorId, room, startAt, duration, status, cancelledAt, note);
                    sharedLessons.Add(key, lesson);
                    db.Lessons.Add(lesson);
                }
            }
            else
            {
                lesson = CreateLesson(tutorId, room, startAt, duration, status, cancelledAt, note);
                db.Lessons.Add(lesson);
            }

            if (lesson.Participants.Any(participant => participant.Student == student))
            {
                throw new InvalidDataException(
                    $"Lesson '{sourceId}' duplicates student '{studentName}' in the same shared lesson.");
            }

            lesson.Participants.Add(new LessonParticipant { Lesson = lesson, Student = student });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Dictionary<string, Tutor>> ImportTutorsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var tutors = await db.Tutors.ToDictionaryAsync(
            tutor => tutor.Id,
            StringComparer.OrdinalIgnoreCase,
            cancellationToken);

        await foreach (var row in ReadCsvAsync(path, cancellationToken))
        {
            var id = Required(row, "tutor_id", path);
            var name = Required(row, "tutor_name", path);

            if (tutors.TryGetValue(id, out var existingTutor))
            {
                existingTutor.Name = name;
                continue;
            }

            var tutor = new Tutor { Id = id, Name = name };
            tutors.Add(id, tutor);
            db.Tutors.Add(tutor);
        }

        return tutors;
    }

    private static Lesson CreateLesson(
        string tutorId,
        string room,
        DateTime startAt,
        int duration,
        LessonStatus status,
        DateTimeOffset? cancelledAt,
        string? note) => new()
        {
            TutorId = tutorId,
            Room = room,
            StartAt = startAt,
            DurationMinutes = duration,
            Status = status,
            CancelledAt = cancelledAt,
            Note = note
        };

    private static DateOnly ParseDate(string value, string sourceId)
    {
        if (DateOnly.TryParseExact(
                value,
                SupportedDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        throw new InvalidDataException(
            $"Lesson '{sourceId}' has invalid date '{value}'. Expected dd-MM-yy or yyyy-MM-dd.");
    }

    private static TimeOnly ParseTime(string value, string sourceId)
    {
        if (TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        throw new InvalidDataException($"Lesson '{sourceId}' has invalid start time '{value}'. Expected HH:mm.");
    }

    private static int ParsePositiveInteger(string value, string sourceId)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result > 0)
        {
            return result;
        }

        throw new InvalidDataException($"Lesson '{sourceId}' has invalid duration '{value}'.");
    }

    private static LessonStatus ParseStatus(string value, string sourceId) => value switch
    {
        "booked" => LessonStatus.Booked,
        "cancelled" => LessonStatus.Cancelled,
        "no_show" => LessonStatus.NoShow,
        _ => throw new InvalidDataException($"Lesson '{sourceId}' has unknown status '{value}'.")
    };

    private static DateTimeOffset? ParseCancelledAt(string? value, string sourceId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:sszzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var cancelledAt))
        {
            return cancelledAt;
        }

        throw new InvalidDataException($"Lesson '{sourceId}' has invalid cancelled_at value '{value}'.");
    }

    private static string Required(IReadOnlyDictionary<string, string> row, string column, string path)
    {
        var value = Optional(row, column);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Missing required '{column}' value in '{path}'.");
    }

    private static string? Optional(IReadOnlyDictionary<string, string> row, string column)
    {
        if (!row.TryGetValue(column, out var value))
        {
            throw new InvalidDataException($"CSV is missing required column '{column}'.");
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, string>> ReadCsvAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Seed CSV file was not found.", path);
        }

        using var reader = new StreamReader(path);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine is null)
        {
            throw new InvalidDataException($"Seed CSV '{path}' is empty.");
        }

        var headers = ParseCsvLine(headerLine);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = ParseCsvLine(line);
            if (values.Count != headers.Count)
            {
                throw new InvalidDataException($"CSV row in '{path}' has {values.Count} fields; expected {headers.Count}.");
            }

            yield return headers.Zip(values).ToDictionary(
                pair => pair.First.Trim(),
                pair => pair.Second,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        if (inQuotes)
        {
            throw new InvalidDataException("CSV row contains an unterminated quoted field.");
        }

        fields.Add(field.ToString());
        return fields;
    }

    private sealed record SharedLessonKey(
        DateTime StartAt,
        int DurationMinutes,
        string TutorId,
        string Room,
        LessonStatus Status,
        string Note);
}
