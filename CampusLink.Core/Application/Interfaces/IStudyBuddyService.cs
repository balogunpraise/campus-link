using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Application.Dtos.StudyBuddies;
using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Application.Interfaces;

public interface IStudyBuddyService
{
    Task<Result<StudyBuddyProfileResponse>> UpsertProfileAsync(
        Guid userId,
        UpsertStudyBuddyProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<StudyBuddyProfileResponse>> GetMyProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    // Students who share at least one of the caller's interests and whose
    // visibility settings allow the caller to see them.
    Task<Result<PagedResult<StudyBuddyCandidate>>> FindCandidatesAsync(
        Guid userId,
        StudyBuddyCandidateRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<StudyBuddyMatchResponse>> SendRequestAsync(
        Guid requesterId,
        SendStudyBuddyRequestRequest request,
        CancellationToken cancellationToken = default);

    // Accept or decline. Only the recipient may answer.
    Task<Result<StudyBuddyMatchResponse>> RespondAsync(
        Guid userId,
        Guid matchId,
        bool accept,
        CancellationToken cancellationToken = default);

    // Either side can end an accepted pairing, or the requester can withdraw a
    // request that hasn't been answered.
    Task<Result<StudyBuddyMatchResponse>> EndAsync(
        Guid userId,
        Guid matchId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<StudyBuddyMatchResponse>>> GetMyMatchesAsync(
        Guid userId,
        MatchStatus? status,
        CancellationToken cancellationToken = default);
}
