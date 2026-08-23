using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Application.Dtos.StudyBuddies;
using CampusLink.Core.Application.Interfaces;
using CampusLink.Core.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusLink.Api.Controllers;

[ApiController]
[Route("api/study-buddies")]
[Authorize]
public class StudyBuddiesController(IStudyBuddyService studyBuddies) : ControllerBase
{
    // The caller's own matching profile. The subjects listed here are the
    // interests everything else keys off.
    [HttpPut("profile")]
    [ProducesResponseType(typeof(StudyBuddyProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertProfile(
        UpsertStudyBuddyProfileRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await studyBuddies.UpsertProfileAsync(User.GetUserId(), request, cancellationToken));

    [HttpGet("profile")]
    [ProducesResponseType(typeof(StudyBuddyProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken) =>
        ToActionResult(await studyBuddies.GetMyProfileAsync(User.GetUserId(), cancellationToken));

    // Students who share at least one interest with the caller and whose
    // visibility settings allow being found.
    [HttpGet("candidates")]
    [ProducesResponseType(typeof(PagedResult<StudyBuddyCandidate>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Candidates(
        [FromQuery] StudyBuddyCandidateRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await studyBuddies.FindCandidatesAsync(User.GetUserId(), request, cancellationToken));

    [HttpPost("requests")]
    [ProducesResponseType(typeof(StudyBuddyMatchResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendRequest(
        SendStudyBuddyRequestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await studyBuddies.SendRequestAsync(User.GetUserId(), request, cancellationToken);

        return result.Succeeded
            ? CreatedAtAction(nameof(GetMatches), new { }, result.Value)
            : ToProblem(result);
    }

    // Incoming and outgoing, filterable by status.
    [HttpGet("requests")]
    [ProducesResponseType(typeof(IReadOnlyList<StudyBuddyMatchResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMatches(
        [FromQuery] MatchStatus? status,
        CancellationToken cancellationToken) =>
        ToActionResult(await studyBuddies.GetMyMatchesAsync(User.GetUserId(), status, cancellationToken));

    // Accept or decline — recipient only.
    [HttpPost("requests/{id:guid}/respond")]
    [ProducesResponseType(typeof(StudyBuddyMatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Respond(
        Guid id,
        RespondToStudyBuddyRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await studyBuddies.RespondAsync(User.GetUserId(), id, request.Accept, cancellationToken));

    // Withdraw a pending request, or end an accepted pairing. Either side.
    [HttpPost("requests/{id:guid}/end")]
    [ProducesResponseType(typeof(StudyBuddyMatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> End(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await studyBuddies.EndAsync(User.GetUserId(), id, cancellationToken));

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
