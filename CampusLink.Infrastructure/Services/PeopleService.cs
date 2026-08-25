using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Application.Dtos.People;
using CampusLink.Core.Application.Dtos.StudyBuddies;
using CampusLink.Core.Application.Interfaces;
using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Services;

public class PeopleService(IRepositoryManager repository) : IPeopleService
{
    public async Task<Result<PagedResult<PersonSummary>>> SearchAsync(
        Guid userId,
        PeopleSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var me = await repository.Users.GetWithInstitutionAsync(userId, false, cancellationToken);

        if (me is null)
            return Result<PagedResult<PersonSummary>>.Failure(AuthErrorCode.NotFound, "User not found.");

        var query = repository.Users.Search(request.Search, false)
            .Where(u => u.Id != userId);

        // ----- visibility -----
        //
        // Your own school is always searchable. Everyone else has to be a
        // verified member who hasn't opted out, at a school that permits
        // cross-institution discovery. Unverified accounts stay invisible
        // outside their own campus, which is what stops a fake sign-up from
        // browsing the global directory.
        query = query.Where(u =>
            u.InstitutionId == me.InstitutionId ||
            (u.AllowCrossInstitutionDiscovery &&
             u.MembershipStatus == InstitutionMembershipStatus.Verified &&
             u.Institution.AllowsCrossInstitutionSharing &&
             me.MembershipStatus == InstitutionMembershipStatus.Verified));

        if (request.SameInstitutionOnly)
            query = query.Where(u => u.InstitutionId == me.InstitutionId);
        else if (request.InstitutionId.HasValue)
            query = query.Where(u => u.InstitutionId == request.InstitutionId.Value);

        if (!string.IsNullOrWhiteSpace(request.Department))
            query = query.Where(u => u.Department == request.Department);

        if (request.YearOfStudy.HasValue)
            query = query.Where(u => u.YearOfStudy == request.YearOfStudy.Value);

        if (request.StudyBuddiesOnly)
            query = query.Where(u => u.StudyBuddyProfile != null && u.StudyBuddyProfile.IsActive);

        if (request.SubjectId.HasValue)
            query = query.Where(u =>
                u.StudyBuddyProfile != null &&
                u.StudyBuddyProfile.PreferredSubjects.Any(s => s.SubjectId == request.SubjectId.Value));

        var total = await query.CountAsync(cancellationToken);

        var items = await BuildSummariesAsync(
            me, query.Skip(request.Skip).Take(request.PageSize), cancellationToken);

        return Result<PagedResult<PersonSummary>>.Success(new PagedResult<PersonSummary>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = total
        });
    }

    public async Task<Result<PersonSummary>> GetAsync(
        Guid userId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        var me = await repository.Users.GetWithInstitutionAsync(userId, false, cancellationToken);
        var target = await repository.Users.GetWithInstitutionAsync(targetUserId, false, cancellationToken);

        // 404 rather than 403 when they're out of reach: confirming the account
        // exists would leak the directory this rule exists to protect.
        if (me is null || target is null || !IsVisibleTo(me, target))
            return Result<PersonSummary>.Failure(AuthErrorCode.NotFound, "Person not found.");

        var items = await BuildSummariesAsync(
            me,
            repository.Users.FindByCondition(u => u.Id == targetUserId, false),
            cancellationToken);

        return items.Count == 0
            ? Result<PersonSummary>.Failure(AuthErrorCode.NotFound, "Person not found.")
            : Result<PersonSummary>.Success(items[0]);
    }

    // Shared projection: one query for the people, one for my interests, one for
    // existing pairings — rather than per-row lookups.
    private async Task<List<PersonSummary>> BuildSummariesAsync(
        AppUser me,
        IQueryable<AppUser> query,
        CancellationToken cancellationToken)
    {
        var userId = me.Id;

        var rows = await query
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.FirstName,
                u.LastName,
                u.Bio,
                u.ProfilePictureUrl,
                u.InstitutionId,
                InstitutionName = u.Institution.Name,
                u.Institution.CountryCode,
                u.Department,
                u.YearOfStudy,
                u.AllowCrossInstitutionDiscovery,
                u.MembershipStatus,
                HasProfile = u.StudyBuddyProfile != null,
                IsOpen = u.StudyBuddyProfile != null && u.StudyBuddyProfile.IsActive,
                MatchScope = u.StudyBuddyProfile != null
                    ? u.StudyBuddyProfile.MatchScope
                    : (VisibilityScope?)null,
                Subjects = u.StudyBuddyProfile == null
                    ? new List<SubjectSummary>()
                    : u.StudyBuddyProfile.PreferredSubjects.Select(s => new SubjectSummary
                    {
                        Id = s.Subject.Id,
                        Name = s.Subject.Name,
                        Slug = s.Subject.Slug,
                        Field = s.Subject.Field
                    }).ToList()
            })
            .ToListAsync(cancellationToken);

        var myInterests = await repository.StudyBuddyProfiles
            .FindByCondition(p => p.UserId == userId, false)
            .SelectMany(p => p.PreferredSubjects.Select(s => s.SubjectId))
            .ToListAsync(cancellationToken);

        var interestSet = myInterests.ToHashSet();

        // Existing pairings resolved in one query rather than one per row. A
        // pair can have several over time (an ended pairing may be retried), so
        // collapse to the one the client should act on.
        var ids = rows.Select(r => r.Id).ToList();

        var matches = await repository.StudyBuddyMatches
            .FindByCondition(
                m => (m.RequesterId == userId && ids.Contains(m.RecipientId)) ||
                     (m.RecipientId == userId && ids.Contains(m.RequesterId)),
                false)
            .Select(m => new { m.RequesterId, m.RecipientId, m.Status, m.RequestedAt })
            .ToListAsync(cancellationToken);

        var statusByUser = matches
            .GroupBy(m => m.RequesterId == userId ? m.RecipientId : m.RequesterId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(m => m.Status is MatchStatus.Pending or MatchStatus.Accepted)
                    .ThenByDescending(m => m.RequestedAt)
                    .First()
                    .Status);

        var partnerIds = await repository.InstitutionPartnerships
            .GetPartnerInstitutionIdsAsync(me.InstitutionId, cancellationToken);

        var items = rows.Select(r =>
        {
            var existing = statusByUser.TryGetValue(r.Id, out var status) ? status : (MatchStatus?)null;
            var (canSend, reason) = CanSendRequest(
                me, r.InstitutionId, r.HasProfile, r.IsOpen, r.MatchScope, partnerIds, existing);

            return new PersonSummary
            {
                UserId = r.Id,
                UserName = r.UserName,
                FullName = $"{r.FirstName} {r.LastName}".Trim(),
                Bio = r.Bio,
                ProfilePictureUrl = r.ProfilePictureUrl,
                InstitutionId = r.InstitutionId,
                InstitutionName = r.InstitutionName,
                CountryCode = r.CountryCode,
                IsSameInstitution = r.InstitutionId == me.InstitutionId,
                Department = r.Department,
                YearOfStudy = r.YearOfStudy,
                HasStudyBuddyProfile = r.HasProfile,
                IsOpenToStudyBuddies = r.IsOpen,
                Subjects = r.Subjects,
                SharedSubjects = r.Subjects.Where(s => interestSet.Contains(s.Id)).ToList(),
                ExistingMatchStatus = existing?.ToString(),
                CanSendRequest = canSend,
                CannotSendReason = reason
            };
        })
        // Same school first, then people you share interests with — the two
        // things that make a request likely to be accepted.
        .OrderByDescending(p => p.IsSameInstitution)
        .ThenByDescending(p => p.SharedSubjects.Count)
        .ToList();

        return items;
    }

    private static bool IsVisibleTo(AppUser me, AppUser target) =>
        target.Id != me.Id &&
        (target.InstitutionId == me.InstitutionId ||
         (target.AllowCrossInstitutionDiscovery &&
          target.MembershipStatus == InstitutionMembershipStatus.Verified &&
          target.Institution.AllowsCrossInstitutionSharing &&
          me.MembershipStatus == InstitutionMembershipStatus.Verified));

    // Mirrors what StudyBuddyService will actually enforce, so the button state
    // in the UI matches the answer the API would give.
    private static (bool CanSend, string Reason) CanSendRequest(
        AppUser me,
        Guid targetInstitutionId,
        bool hasProfile,
        bool targetIsOpen,
        VisibilityScope? targetScope,
        IReadOnlyList<Guid> partnerInstitutionIds,
        MatchStatus? existing)
    {
        if (existing is MatchStatus.Pending)
            return (false, "A request between you two is already pending.");

        if (existing is MatchStatus.Accepted)
            return (false, "You're already study buddies.");

        // No profile is not a refusal — they simply haven't opted in yet, and an
        // invitation is usually what prompts them to. Only an existing profile
        // switched off counts as opting out.
        if (!targetIsOpen && hasProfile)
            return (false, "This student isn't accepting study buddy requests right now.");

        if (targetInstitutionId == me.InstitutionId)
            return (true, null);

        if (targetScope is null || targetScope == VisibilityScope.Global)
            return (true, null);

        if (targetScope == VisibilityScope.PartnerInstitutions &&
            partnerInstitutionIds.Contains(me.InstitutionId))
            return (true, null);

        return (false, "This student only matches within their own institution.");
    }
}
