using BrightPath.Api.Data;
using BrightPath.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BrightPath.Api.Controllers;

[ApiController]
[Route("api/students")]
public sealed class StudentsController(SchedulingDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<List<StudentResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StudentResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var students = await db.Students.AsNoTracking()
            .OrderBy(student => student.Id)
            .Select(student => new StudentResponse(student.Id, student.Name))
            .ToListAsync(cancellationToken);

        return Ok(students);
    }
}
