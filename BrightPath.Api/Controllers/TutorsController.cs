using BrightPath.Api.Data;
using BrightPath.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BrightPath.Api.Controllers;

[ApiController]
[Route("api/tutors")]
public sealed class TutorsController(SchedulingDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<List<TutorResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TutorResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var tutors = await db.Tutors.AsNoTracking()
            .OrderBy(tutor => tutor.Id)
            .Select(tutor => new TutorResponse(tutor.Id, tutor.Name))
            .ToListAsync(cancellationToken);

        return Ok(tutors);
    }
}
