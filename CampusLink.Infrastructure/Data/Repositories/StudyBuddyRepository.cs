using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data.Repositories;

public class StudyBuddyProfileRepository(AppDbContext context)
    : RepositoryBase<StudyBuddyProfile>(context), IStudyBuddyProfileRepository
{
    public Task<StudyBuddyProfile> GetForUserAsync(
        Guid userId,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(p => p.UserId == userId, trackChanges)
            .Include(p => p.PreferredSubjects)
            .FirstOrDefaultAsync(cancellationToken);

    public IQueryable<StudyBuddyProfile> FindCandidates(
        Guid seekerUserId,
        Guid seekerInstitutionId,
        IReadOnlyCollection<Guid> partnerInstitutionIds,
        IReadOnlyCollection<Guid> subjectIds,
        bool trackChanges)
    {
        var partnerIds = partnerInstitutionIds ?? Array.Empty<Guid>();
        var subjects = subjectIds ?? Array.Empty<Guid>();

        IQueryable<StudyBuddyProfile> query = FindByCondition(p => p.IsActive && p.UserId != seekerUserId, trackChanges)
            .Include(p => p.User)
            .Include(p => p.PreferredSubjects);

        // Overlap on at least one wanted subject. Subjects (not courses) are
        // what make this work across schools.
        if (subjects.Count > 0)
            query = query.Where(p => p.PreferredSubjects.Any(s => subjects.Contains(s.SubjectId)));

        // Same school: always allowed. Another school: the candidate must be
        // opted in, verified, and their MatchScope has to reach the seeker.
        query = query.Where(p =>
            p.User.InstitutionId == seekerInstitutionId ||
            (p.User.AllowCrossInstitutionDiscovery &&
             p.User.MembershipStatus == InstitutionMembershipStatus.Verified &&
             p.User.Institution.AllowsCrossInstitutionSharing &&
             (p.MatchScope == VisibilityScope.Global ||
              (p.MatchScope == VisibilityScope.PartnerInstitutions &&
               partnerIds.Contains(p.User.InstitutionId)))));

        // Same-school candidates first — easier to actually meet up with.
        return query.OrderByDescending(p => p.User.InstitutionId == seekerInstitutionId);
    }

    public Task<StudyBuddyProfile> GetWithSubjectsAsync(
        Guid profileId,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(p => p.Id == profileId, trackChanges)
            .Include(p => p.PreferredSubjects)
            .Include(p => p.Languages)
            .Include(p => p.User)
            .FirstOrDefaultAsync(cancellationToken);
}

public class StudyBuddyMatchRepository(AppDbContext context)
    : RepositoryBase<StudyBuddyMatch>(context), IStudyBuddyMatchRepository
{
    public Task<StudyBuddyMatch> GetBetweenAsync(
        Guid userAId,
        Guid userBId,
        CancellationToken cancellationToken = default) =>
        // Direction doesn't matter: a pairing is a pairing whoever asked.
        FindByCondition(
                m => (m.RequesterId == userAId && m.RecipientId == userBId) ||
                     (m.RequesterId == userBId && m.RecipientId == userAId),
                false)
            .OrderByDescending(m => m.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public IQueryable<StudyBuddyMatch> GetForUser(Guid userId, MatchStatus? status, bool trackChanges)
    {
        var query = FindByCondition(m => m.RequesterId == userId || m.RecipientId == userId, trackChanges);

        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);

        return query.OrderByDescending(m => m.RequestedAt);
    }

    public IQueryable<StudyBuddyMatch> GetPendingForRecipient(Guid recipientId, bool trackChanges) =>
        FindByCondition(m => m.RecipientId == recipientId && m.Status == MatchStatus.Pending, trackChanges)
            .Include(m => m.Requester)
            .OrderByDescending(m => m.RequestedAt);
}

public class StudyGroupRepository(AppDbContext context)
    : RepositoryBase<StudyGroup>(context), IStudyGroupRepository
{
    public IQueryable<StudyGroup> GetDiscoverableBy(
        Guid viewerInstitutionId,
        IReadOnlyCollection<Guid> partnerInstitutionIds,
        bool trackChanges)
    {
        var partnerIds = partnerInstitutionIds ?? Array.Empty<Guid>();

        return FindAll(trackChanges)
            .Where(g =>
                g.HostInstitutionId == viewerInstitutionId ||
                g.Visibility == VisibilityScope.Global ||
                (g.Visibility == VisibilityScope.PartnerInstitutions &&
                 partnerIds.Contains(g.HostInstitutionId)))
            .OrderByDescending(g => g.CreatedAt);
    }

    public IQueryable<StudyGroup> GetForSubject(Guid subjectId, bool trackChanges) =>
        FindByCondition(g => g.SubjectId == subjectId, trackChanges)
            .OrderByDescending(g => g.CreatedAt);

    public IQueryable<StudyGroup> GetMembershipsOf(Guid userId, bool trackChanges) =>
        FindAll(trackChanges)
            .Where(g => g.Members.Any(m => m.UserId == userId))
            .OrderByDescending(g => g.CreatedAt);

    public Task<StudyGroup> GetWithMembersAsync(
        Guid studyGroupId,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(g => g.Id == studyGroupId, trackChanges)
            .Include(g => g.Members)
            .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountMembersAsync(Guid studyGroupId, CancellationToken cancellationToken = default) =>
        Context.StudyGroupMembers
            .AsNoTracking()
            .CountAsync(m => m.StudyGroupId == studyGroupId, cancellationToken);
}
