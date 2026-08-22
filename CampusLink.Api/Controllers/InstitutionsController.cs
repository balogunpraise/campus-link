using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Application.Dtos.Institutions;
using CampusLink.Core.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CampusLink.Api.Controllers;

// Anonymous by necessity: a student has to find their school before they can
// have an account. Rate-limited for the same reason — nothing here is behind a
// token, so without a cap this is a free scrape of the whole directory.
[ApiController]
[Route("api/institutions")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.PublicRead)]
public class InstitutionsController(IInstitutionService institutions) : ControllerBase
{
    // GET /api/institutions?search=lag&countryCode=NG&page=1&pageSize=20
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InstitutionSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] InstitutionSearchRequest request,
        CancellationToken cancellationToken) =>
        Ok(await institutions.SearchAsync(request, cancellationToken));

    // Countries that actually have schools, most-populated first — the country
    // selector, without a hardcoded ISO list.
    [HttpGet("countries")]
    [ProducesResponseType(typeof(IReadOnlyList<CountrySummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Countries(CancellationToken cancellationToken) =>
        Ok(await institutions.GetCountriesAsync(cancellationToken));

    // Called when the sign-up email field blurs: if this resolves, the client
    // hides the institution picker entirely.
    [HttpGet("resolve")]
    [ProducesResponseType(typeof(InstitutionResolveResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resolve(
        [FromQuery] string email,
        CancellationToken cancellationToken) =>
        Ok(await institutions.ResolveByEmailAsync(email, cancellationToken));

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(InstitutionDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var result = await institutions.GetBySlugAsync(slug, cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            : Problem(title: result.ErrorCode.ToString(), statusCode: StatusCodes.Status404NotFound);
    }

    // "I can't find my school." Creates it unverified, pending moderation.
    // Tighter limit than the read endpoints: this one writes.
    [HttpPost("proposals")]
    [EnableRateLimiting(RateLimitPolicies.PublicWrite)]
    [ProducesResponseType(typeof(InstitutionSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Propose(
        ProposeInstitutionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await institutions.ProposeAsync(request, cancellationToken);

        if (!result.Succeeded)
            return Problem(
                title: result.ErrorCode.ToString(),
                detail: string.Join(" ", result.Errors),
                statusCode: result.ErrorCode == AuthErrorCode.Conflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest);

        return CreatedAtAction(nameof(GetBySlug), new { slug = result.Value.Slug }, result.Value);
    }
}
