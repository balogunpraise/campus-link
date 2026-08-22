using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Application.Interfaces.Repositories;

public interface IStudyBuddyProfileRepository : IRepositoryBase<StudyBuddyProfile>
{
    Task<StudyBuddyProfile> GetForUserAsync(
        Guid userId,
        bool trackChanges,
        CancellationToken cancellationToken = default);

    // The matcher's candidate pool. Returns active profiles interested in one of
    // the given subjects, honouring both sides of the visibility deal: the
    // candidate's MatchScope must reach the seeker's institution, and the
    // candidate must not have opted out of cross-institution discovery.
    IQueryable<StudyBuddyProfile> FindCandidates(
        Guid seekerUserId,
        Guid seekerInstitutionId,
        IReadOnlyCollection<Guid> partnerInstitutionIds,
        IReadOnlyCollection<Guid> subjectIds,
        bool trackChanges);

    Task<StudyBuddyProfile> GetWithSubjectsAsync(
        Guid profileId,
        bool trackChanges,
        CancellationToken cancellationToken = default);
}

public interface IStudyBuddyMatchRepository : IRepositoryBase<StudyBuddyMatch>
{
    // Either direction — a pairing exists regardless of who asked first, so this
    // is what blocks duplicate requests.
    Task<StudyBuddyMatch> GetBetweenAsync(
        Guid userAId,
        Guid userBId,
        CancellationToken cancellationToken = default);

    IQueryable<StudyBuddyMatch> GetForUser(Guid userId, MatchStatus? status, bool trackChanges);

    IQueryable<StudyBuddyMatch> GetPendingForRecipient(Guid recipientId, bool trackChanges);
}

public interface IStudyGroupRepository : IRepositoryBase<StudyGroup>
{
    // Groups the viewer may join: their own school's, partner schools' where the
    // group allows it, and Global ones from anywhere.
    IQueryable<StudyGroup> GetDiscoverableBy(
        Guid viewerInstitutionId,
        IReadOnlyCollection<Guid> partnerInstitutionIds,
        bool trackChanges);

    IQueryable<StudyGroup> GetForSubject(Guid subjectId, bool trackChanges);

    IQueryable<StudyGroup> GetMembershipsOf(Guid userId, bool trackChanges);

    Task<StudyGroup> GetWithMembersAsync(
        Guid studyGroupId,
        bool trackChanges,
        CancellationToken cancellationToken = default);

    Task<int> CountMembersAsync(Guid studyGroupId, CancellationToken cancellationToken = default);
}
