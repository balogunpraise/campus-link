using CampusLink.Core.Application.Dtos.StudyBuddies;
using CampusLink.Core.Application.Interfaces.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Api.Controllers;

// The shared taxonomy students pick their interests from. Read-only and
// anonymous: the sign-up flow shows it before an account exists.
[ApiController]
[Route("api/subjects")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.PublicRead)]
public class SubjectsController(IRepositoryManager repository) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SubjectSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string search,
        [FromQuery] string field,
        CancellationToken cancellationToken)
    {
        var subjects = await repository.Subjects
            .SearchApproved(search, field, false)
            .Take(200)
            .Select(s => new SubjectSummary
            {
                Id = s.Id,
                Name = s.Name,
                Slug = s.Slug,
                Field = s.Field
            })
            .ToListAsync(cancellationToken);

        return Ok(subjects);
    }

    // Field groupings, for a two-level picker.
    [HttpGet("fields")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Fields(CancellationToken cancellationToken)
    {
        var fields = await repository.Subjects
            .FindByCondition(s => s.IsApproved && s.Field != null, false)
            .Select(s => s.Field)
            .Distinct()
            .OrderBy(f => f)
            .ToListAsync(cancellationToken);

        return Ok(fields);
    }
}
