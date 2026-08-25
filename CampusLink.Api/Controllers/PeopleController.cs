using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Application.Dtos.People;
using CampusLink.Core.Application.Dtos.StudyBuddies;
using CampusLink.Core.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusLink.Api.Controllers;

// Finding a specific person by name. Authenticated — unlike the institution
// directory, this exposes students, so it's never anonymous.
[ApiController]
[Route("api/people")]
[Authorize]
public class PeopleController(IPeopleService people, IStudyBuddyService studyBuddies) : ControllerBase
{
    // GET /api/people?search=ada&sameInstitutionOnly=true&subjectId=...
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PersonSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] PeopleSearchRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await people.SearchAsync(User.GetUserId(), request, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PersonSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await people.GetAsync(User.GetUserId(), id, cancellationToken));

    // Send a study buddy request straight from a search result. Same service and
    // same rules as the interest-matching flow — `subjectId` is just optional
    // here, because you picked the person rather than the interest.
    [HttpPost("{id:guid}/study-buddy-request")]
    [ProducesResponseType(typeof(StudyBuddyMatchResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendRequest(
        Guid id,
        SendPersonRequestBody body,
        CancellationToken cancellationToken)
    {
        var request = new SendStudyBuddyRequestRequest
        {
            RecipientUserId = id,
            SubjectId = body?.SubjectId,
            CourseId = body?.CourseId,
            MeetingMode = body?.MeetingMode,
            Message = body?.Message
        };

        var result = await studyBuddies.SendRequestAsync(User.GetUserId(), request, cancellationToken);

        return result.Succeeded
            ? CreatedAtAction(nameof(Get), new { id }, result.Value)
            : ToProblem(result);
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.Succeeded ? Ok(result.Value) : ToProblem(result);

    private IActionResult ToProblem<T>(Result<T> result)
    {
        var status = result.ErrorCode switch
        {
            AuthErrorCode.NotFound => StatusCodes.Status404NotFound,
            AuthErrorCode.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(
            title: result.ErrorCode.ToString(),
            detail: string.Join(" ", result.Errors),
            statusCode: status);
    }
}

// The recipient comes from the route, so the body only carries the optional
// extras.
public class SendPersonRequestBody
{
    public Guid? SubjectId { get; set; }
    public Guid? CourseId { get; set; }
    public CampusLink.Core.Domain.Enums.MeetingMode? MeetingMode { get; set; }
    public string Message { get; set; }
}
