using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Application.Dtos.StudyBuddies;
using CampusLink.Core.Application.Interfaces;
using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Entities.LinkingEntities;
using CampusLink.Core.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusLink.Infrastructure.Services;

public class StudyBuddyService(
    IRepositoryManager repository,
    UserManager<AppUser> userManager,
    ILogger<StudyBuddyService> logger) : IStudyBuddyService
{
    // A student with dozens of unanswered requests out is spamming, not looking
    // for a study partner.
    private const int MaxOutstandingRequests = 20;

    public async Task<Result<StudyBuddyProfileResponse>> UpsertProfileAsync(
        Guid userId,
        UpsertStudyBuddyProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var subjectIds = request.SubjectIds.Distinct().ToList();

        // Interests have to come from the shared taxonomy, otherwise matching
        // across schools falls apart.
        var knownSubjects = await repository.Subjects
            .FindByCondition(s => subjectIds.Contains(s.Id) && s.IsApproved, false)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var unknown = subjectIds.Except(knownSubjects).ToList();
        if (unknown.Count > 0)
            return Result<StudyBuddyProfileResponse>.Failure(
                AuthErrorCode.ValidationFailed,
                $"Unknown or unapproved subject(s): {string.Join(", ", unknown)}");

        // Tracked, with both child collections loaded — Clear() below can only
        // delete rows EF knows about, so a missing Include would silently turn
        // "replace my languages" into "insert duplicates" and violate the
        // composite primary key.
        var profile = await repository.StudyBuddyProfiles
            .FindByCondition(p => p.UserId == userId, true)
            .Include(p => p.PreferredSubjects)
            .Include(p => p.Languages)
            .FirstOrDefaultAsync(cancellationToken);

        var isNew = profile is null;

        if (isNew)
        {
            profile = new StudyBuddyProfile { UserId = userId };
            await repository.StudyBuddyProfiles.CreateAsync(profile, cancellationToken);
        }

        profile.LearningStyle = request.LearningStyle ?? string.Empty;
        profile.AvailabilityNotes = request.AvailabilityNotes ?? string.Empty;
        profile.IsActive = request.IsActive;
        profile.MatchScope = request.MatchScope;
        profile.PreferredMeetingMode = request.PreferredMeetingMode;
        profile.MaxTimeZoneOffsetHours = request.MaxTimeZoneOffsetHours;

        // Wholesale replace: the client sends the full set it wants.
        profile.PreferredSubjects.Clear();
        foreach (var subjectId in subjectIds)
            profile.PreferredSubjects.Add(new StudyBuddySubject { SubjectId = subjectId });

        profile.Languages.Clear();
        foreach (var code in request.LanguageCodes.Where(c => !string.IsNullOrWhiteSpace(c))
                     .Select(c => c.Trim().ToLowerInvariant())
                     .Distinct())
            profile.Languages.Add(new StudyBuddyLanguage { LanguageCode = code });

        if (!isNew)
            repository.StudyBuddyProfiles.Update(profile);

        await repository.SaveAsync(cancellationToken);

        return await GetMyProfileAsync(userId, cancellationToken);
    }

    public async Task<Result<StudyBuddyProfileResponse>> GetMyProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await LoadProfileGraphAsync(userId, cancellationToken);

        return profile is null
            ? Result<StudyBuddyProfileResponse>.Failure(
                AuthErrorCode.NotFound, "You don't have a study buddy profile yet.")
            : Result<StudyBuddyProfileResponse>.Success(ToProfileResponse(profile));
    }

    public async Task<Result<PagedResult<StudyBuddyCandidate>>> FindCandidatesAsync(
        Guid userId,
        StudyBuddyCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        var me = await LoadProfileGraphAsync(userId, cancellationToken);

        if (me is null)
            return Result<PagedResult<StudyBuddyCandidate>>.Failure(
                AuthErrorCode.NotFound,
                "Create your study buddy profile first — matching runs on the interests you list.");

        var myInterests = me.PreferredSubjects.Select(s => s.SubjectId).ToHashSet();

        // Default: everything I study. Narrowed: only the subjects asked for,
        // intersected with mine so nobody can search on interests they don't have.
        var wanted = request.SubjectIds.Count > 0
            ? request.SubjectIds.Where(myInterests.Contains).ToList()
            : myInterests.ToList();

        if (wanted.Count == 0)
            return Result<PagedResult<StudyBuddyCandidate>>.Failure(
                AuthErrorCode.ValidationFailed,
                "None of the requested subjects are on your profile.");

        var partnerIds = await repository.InstitutionPartnerships
            .GetPartnerInstitutionIdsAsync(me.User.InstitutionId, cancellationToken);

        var query = repository.StudyBuddyProfiles.FindCandidates(
            userId, me.User.InstitutionId, partnerIds, wanted, false);

        if (request.SameInstitutionOnly)
            query = query.Where(p => p.User.InstitutionId == me.User.InstitutionId);

        if (request.MeetingMode.HasValue)
        {
            // Hybrid works with anything, so it always stays in.
            var mode = request.MeetingMode.Value;
            query = query.Where(p =>
                p.PreferredMeetingMode == mode || p.PreferredMeetingMode == MeetingMode.Hybrid);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(p => new
            {
                p.UserId,
                p.User.UserName,
                p.User.FirstName,
                p.User.LastName,
                p.User.Bio,
                p.User.ProfilePictureUrl,
                p.User.InstitutionId,
                InstitutionName = p.User.Institution.Name,
                p.LearningStyle,
                p.AvailabilityNotes,
                p.PreferredMeetingMode,
                Shared = p.PreferredSubjects
                    .Where(s => wanted.Contains(s.SubjectId))
                    .Select(s => new SubjectSummary
                    {
                        Id = s.Subject.Id,
                        Name = s.Subject.Name,
                        Slug = s.Subject.Slug,
                        Field = s.Subject.Field
                    })
                    .ToList(),
                Languages = p.Languages.Select(l => l.LanguageCode).ToList()
            })
            .ToListAsync(cancellationToken);

        // Existing pairings in one query rather than one per candidate, so the
        // client can render "Pending" instead of offering a button that 409s.
        var candidateIds = rows.Select(r => r.UserId).ToList();
        var existing = await repository.StudyBuddyMatches
            .FindByCondition(
                m => (m.RequesterId == userId && candidateIds.Contains(m.RecipientId)) ||
                     (m.RecipientId == userId && candidateIds.Contains(m.RequesterId)),
                false)
            .Select(m => new { m.RequesterId, m.RecipientId, m.Status })
            .ToListAsync(cancellationToken);

        var statusByUser = existing.ToDictionary(
            m => m.RequesterId == userId ? m.RecipientId : m.RequesterId,
            m => m.Status.ToString());

        var items = rows.Select(r => new StudyBuddyCandidate
        {
            UserId = r.UserId,
            UserName = r.UserName,
            FullName = $"{r.FirstName} {r.LastName}".Trim(),
            Bio = r.Bio,
            ProfilePictureUrl = r.ProfilePictureUrl,
            InstitutionId = r.InstitutionId,
            InstitutionName = r.InstitutionName,
            IsSameInstitution = r.InstitutionId == me.User.InstitutionId,
            LearningStyle = r.LearningStyle,
            AvailabilityNotes = r.AvailabilityNotes,
            PreferredMeetingMode = r.PreferredMeetingMode.ToString(),
            SharedSubjects = r.Shared,
            SharedSubjectCount = r.Shared.Count,
            LanguageCodes = r.Languages,
            ExistingMatchStatus = statusByUser.GetValueOrDefault(r.UserId)
        })
        // More overlap is a better match; the repository already put
        // same-institution candidates first.
        .OrderByDescending(c => c.IsSameInstitution)
        .ThenByDescending(c => c.SharedSubjectCount)
        .ToList();

        return Result<PagedResult<StudyBuddyCandidate>>.Success(new PagedResult<StudyBuddyCandidate>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = total
        });
    }

    public async Task<Result<StudyBuddyMatchResponse>> SendRequestAsync(
        Guid requesterId,
        SendStudyBuddyRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RecipientUserId == requesterId)
            return Fail("You can't send a study buddy request to yourself.", AuthErrorCode.ValidationFailed);

        var me = await LoadProfileGraphAsync(requesterId, cancellationToken);
        if (me is null)
            return Fail(
                "Create your study buddy profile first — a request has to be about a shared interest.",
                AuthErrorCode.ValidationFailed);

        var them = await LoadProfileGraphAsync(request.RecipientUserId, cancellationToken);
        if (them is null || !them.IsActive)
            return Fail("That student isn't looking for a study buddy right now.", AuthErrorCode.NotFound);

        // The heart of the feature: the request must be about an interest both
        // students actually list.
        if (me.PreferredSubjects.All(s => s.SubjectId != request.SubjectId))
            return Fail("That subject isn't on your profile.", AuthErrorCode.ValidationFailed);

        if (them.PreferredSubjects.All(s => s.SubjectId != request.SubjectId))
            return Fail("You and that student don't share that interest.", AuthErrorCode.ValidationFailed);

        var visibility = await CanReachAsync(me, them, cancellationToken);
        if (!visibility.Succeeded)
            return Result<StudyBuddyMatchResponse>.Failure(visibility.ErrorCode, visibility.Errors);

        // One live pairing per couple, in either direction. A previously ended
        // or declined pairing may be retried.
        var existing = await repository.StudyBuddyMatches
            .GetBetweenAsync(requesterId, request.RecipientUserId, cancellationToken);

        if (existing is not null && existing.Status is MatchStatus.Pending or MatchStatus.Accepted)
            return Fail(
                existing.Status == MatchStatus.Pending
                    ? "There's already a pending request between you two."
                    : "You're already study buddies with that student.",
                AuthErrorCode.Conflict);

        var outstanding = await repository.StudyBuddyMatches
            .FindByCondition(m => m.RequesterId == requesterId && m.Status == MatchStatus.Pending, false)
            .CountAsync(cancellationToken);

        if (outstanding >= MaxOutstandingRequests)
            return Fail(
                $"You have {outstanding} requests still waiting for a reply. Wait for some to be answered first.",
                AuthErrorCode.Conflict);

        var isCrossInstitution = me.User.InstitutionId != them.User.InstitutionId;

        // A course belongs to one school, so it can't describe a cross-school
        // pairing — the database enforces this too.
        if (isCrossInstitution && request.CourseId.HasValue)
            return Fail(
                "A course can't be attached to a request between different institutions.",
                AuthErrorCode.ValidationFailed);

        var match = new StudyBuddyMatch
        {
            RequesterId = requesterId,
            RecipientId = request.RecipientUserId,
            SubjectId = request.SubjectId,
            CourseId = request.CourseId,
            IsCrossInstitution = isCrossInstitution,

            // Students at different schools can't meet in person, whatever
            // either profile prefers.
            MeetingMode = isCrossInstitution
                ? MeetingMode.Online
                : request.MeetingMode ?? me.PreferredMeetingMode,

            Status = MatchStatus.Pending,
            Message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim()
        };

        await repository.StudyBuddyMatches.CreateAsync(match, cancellationToken);

        await NotifyAsync(
            request.RecipientUserId,
            NotificationType.MatchRequestReceived,
            $"{FullName(me.User)} wants to be your study buddy.",
            match.Id,
            cancellationToken);

        await repository.SaveAsync(cancellationToken);

        logger.LogInformation(
            "Study buddy request {MatchId} sent from {RequesterId} to {RecipientId} (cross-institution: {Cross})",
            match.Id, requesterId, request.RecipientUserId, isCrossInstitution);

        return Result<StudyBuddyMatchResponse>.Success(
            await ToMatchResponseAsync(match, requesterId, cancellationToken));
    }

    public async Task<Result<StudyBuddyMatchResponse>> RespondAsync(
        Guid userId,
        Guid matchId,
        bool accept,
        CancellationToken cancellationToken = default)
    {
        var match = await repository.StudyBuddyMatches.FindByIdAsync(matchId, true, cancellationToken);

        if (match is null)
            return Fail("Request not found.", AuthErrorCode.NotFound);

        // Only the person who received it can answer. Returning NotFound rather
        // than Forbidden avoids confirming the id exists to a stranger.
        if (match.RecipientId != userId)
            return Fail("Request not found.", AuthErrorCode.NotFound);

        if (match.Status != MatchStatus.Pending)
            return Fail($"That request has already been {match.Status.ToString().ToLowerInvariant()}.",
                AuthErrorCode.Conflict);

        match.Status = accept ? MatchStatus.Accepted : MatchStatus.Declined;
        match.RespondedAt = DateTime.UtcNow;
        repository.StudyBuddyMatches.Update(match);

        var responder = await userManager.FindByIdAsync(userId.ToString());

        await NotifyAsync(
            match.RequesterId,
            NotificationType.MatchRequestUpdated,
            accept
                ? $"{FullName(responder)} accepted your study buddy request."
                : $"{FullName(responder)} declined your study buddy request.",
            match.Id,
            cancellationToken);

        await repository.SaveAsync(cancellationToken);

        return Result<StudyBuddyMatchResponse>.Success(
            await ToMatchResponseAsync(match, userId, cancellationToken));
    }

    public async Task<Result<StudyBuddyMatchResponse>> EndAsync(
        Guid userId,
        Guid matchId,
        CancellationToken cancellationToken = default)
    {
        var match = await repository.StudyBuddyMatches.FindByIdAsync(matchId, true, cancellationToken);

        if (match is null || (match.RequesterId != userId && match.RecipientId != userId))
            return Fail("Request not found.", AuthErrorCode.NotFound);

        if (match.Status is MatchStatus.Declined or MatchStatus.Ended)
            return Fail("That pairing is already closed.", AuthErrorCode.Conflict);

        // Withdrawing your own unanswered request is a cancel, not a breakup —
        // but both land in the same terminal state.
        var wasPending = match.Status == MatchStatus.Pending;

        match.Status = MatchStatus.Ended;
        match.EndedAt = DateTime.UtcNow;
        repository.StudyBuddyMatches.Update(match);

        var otherUserId = match.RequesterId == userId ? match.RecipientId : match.RequesterId;
        var actor = await userManager.FindByIdAsync(userId.ToString());

        // No point notifying about a request the other side never answered.
        if (!wasPending || match.RecipientId != otherUserId)
            await NotifyAsync(
                otherUserId,
                NotificationType.MatchRequestUpdated,
                $"{FullName(actor)} ended your study buddy pairing.",
                match.Id,
                cancellationToken);

        await repository.SaveAsync(cancellationToken);

        return Result<StudyBuddyMatchResponse>.Success(
            await ToMatchResponseAsync(match, userId, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<StudyBuddyMatchResponse>>> GetMyMatchesAsync(
        Guid userId,
        MatchStatus? status,
        CancellationToken cancellationToken = default)
    {
        var matches = await repository.StudyBuddyMatches
            .GetForUser(userId, status, false)
            .Include(m => m.Requester).ThenInclude(u => u.Institution)
            .Include(m => m.Recipient).ThenInclude(u => u.Institution)
            .Include(m => m.Subject)
            .ToListAsync(cancellationToken);

        var items = matches.Select(m => BuildMatchResponse(m, userId)).ToList();

        return Result<IReadOnlyList<StudyBuddyMatchResponse>>.Success(items);
    }

    // ----- helpers -----

    // Mirrors the repository's candidate filter for the single-target case: a
    // request must respect the same visibility rules the browse list does,
    // otherwise someone could guess a user id and bypass them.
    private async Task<Result<bool>> CanReachAsync(
        StudyBuddyProfile me,
        StudyBuddyProfile them,
        CancellationToken cancellationToken)
    {
        if (me.User.InstitutionId == them.User.InstitutionId)
            return Result<bool>.Success(true);

        if (!them.User.AllowCrossInstitutionDiscovery ||
            them.User.MembershipStatus != InstitutionMembershipStatus.Verified ||
            !them.User.Institution.AllowsCrossInstitutionSharing)
            return Result<bool>.Failure(
                AuthErrorCode.NotFound, "That student isn't available for cross-institution matching.");

        if (them.MatchScope == VisibilityScope.Global)
            return Result<bool>.Success(true);

        if (them.MatchScope == VisibilityScope.PartnerInstitutions)
        {
            var partners = await repository.InstitutionPartnerships
                .GetPartnerInstitutionIdsAsync(them.User.InstitutionId, cancellationToken);

            if (partners.Contains(me.User.InstitutionId))
                return Result<bool>.Success(true);
        }

        return Result<bool>.Failure(
            AuthErrorCode.NotFound, "That student only matches within their own institution.");
    }

    private Task<StudyBuddyProfile> LoadProfileGraphAsync(Guid userId, CancellationToken cancellationToken) =>
        repository.StudyBuddyProfiles
            .FindByCondition(p => p.UserId == userId, false)
            .Include(p => p.User).ThenInclude(u => u.Institution)
            .Include(p => p.PreferredSubjects).ThenInclude(s => s.Subject)
            .Include(p => p.Languages)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task NotifyAsync(
        Guid userId,
        NotificationType type,
        string content,
        Guid matchId,
        CancellationToken cancellationToken) =>
        // Staged, not saved — it commits with the match change in one
        // transaction, so there's never a notification for something that
        // didn't happen.
        await repository.Notifications.CreateAsync(
            new Notification
            {
                UserId = userId,
                Type = type,
                Content = content,
                RelatedEntityId = matchId,
                RelatedEntityType = nameof(StudyBuddyMatch)
            },
            cancellationToken);

    private async Task<StudyBuddyMatchResponse> ToMatchResponseAsync(
        StudyBuddyMatch match,
        Guid viewerId,
        CancellationToken cancellationToken)
    {
        var otherId = match.RequesterId == viewerId ? match.RecipientId : match.RequesterId;

        var other = await userManager.Users
            .Include(u => u.Institution)
            .FirstOrDefaultAsync(u => u.Id == otherId, cancellationToken);

        var subject = match.Subject ?? (match.SubjectId.HasValue
            ? await repository.Subjects.FindByIdAsync(match.SubjectId.Value, false, cancellationToken)
            : null);

        return new StudyBuddyMatchResponse
        {
            Id = match.Id,
            Status = match.Status.ToString(),
            MeetingMode = match.MeetingMode.ToString(),
            IsCrossInstitution = match.IsCrossInstitution,
            Subject = subject is null ? null : ToSubjectSummary(subject),
            Message = match.Message,
            CounterpartUserId = otherId,
            CounterpartUserName = other?.UserName,
            CounterpartFullName = FullName(other),
            CounterpartInstitutionName = other?.Institution?.Name,
            IsOutgoing = match.RequesterId == viewerId,
            RequestedAt = match.RequestedAt,
            RespondedAt = match.RespondedAt,
            EndedAt = match.EndedAt
        };
    }

    private static StudyBuddyMatchResponse BuildMatchResponse(StudyBuddyMatch match, Guid viewerId)
    {
        var isOutgoing = match.RequesterId == viewerId;
        var other = isOutgoing ? match.Recipient : match.Requester;

        return new StudyBuddyMatchResponse
        {
            Id = match.Id,
            Status = match.Status.ToString(),
            MeetingMode = match.MeetingMode.ToString(),
            IsCrossInstitution = match.IsCrossInstitution,
            Subject = match.Subject is null ? null : ToSubjectSummary(match.Subject),
            Message = match.Message,
            CounterpartUserId = other.Id,
            CounterpartUserName = other.UserName,
            CounterpartFullName = FullName(other),
            CounterpartInstitutionName = other.Institution?.Name,
            IsOutgoing = isOutgoing,
            RequestedAt = match.RequestedAt,
            RespondedAt = match.RespondedAt,
            EndedAt = match.EndedAt
        };
    }

    private static StudyBuddyProfileResponse ToProfileResponse(StudyBuddyProfile p) => new()
    {
        Id = p.Id,
        UserId = p.UserId,
        UserName = p.User.UserName,
        FullName = FullName(p.User),
        LearningStyle = p.LearningStyle,
        AvailabilityNotes = p.AvailabilityNotes,
        IsActive = p.IsActive,
        MatchScope = p.MatchScope.ToString(),
        PreferredMeetingMode = p.PreferredMeetingMode.ToString(),
        MaxTimeZoneOffsetHours = p.MaxTimeZoneOffsetHours,
        InstitutionId = p.User.InstitutionId,
        InstitutionName = p.User.Institution?.Name,
        Subjects = p.PreferredSubjects.Where(s => s.Subject is not null)
            .Select(s => ToSubjectSummary(s.Subject)).ToList(),
        LanguageCodes = p.Languages.Select(l => l.LanguageCode).ToList()
    };

    private static SubjectSummary ToSubjectSummary(Subject s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Slug = s.Slug,
        Field = s.Field
    };

    private static string FullName(AppUser user) =>
        user is null ? null : $"{user.FirstName} {user.LastName}".Trim();

    private static Result<StudyBuddyMatchResponse> Fail(string message, AuthErrorCode code) =>
        Result<StudyBuddyMatchResponse>.Failure(code, message);
}
