namespace CampusLink.Core.Application.Interfaces.Repositories;

// Single entry point to every repository, plus the one place changes are
// committed. Services depend on this rather than a dozen repository
// interfaces, and because all repositories share the DbContext, work spanning
// several of them (create a match, then a notification) commits atomically in
// one SaveAsync.
//
// Implementations create each repository lazily, so a request that only touches
// resources doesn't pay for the rest.
public interface IRepositoryManager
{
    IInstitutionRepository Institutions { get; }
    IInstitutionEmailDomainRepository InstitutionEmailDomains { get; }
    IInstitutionPartnershipRepository InstitutionPartnerships { get; }
    ISubjectRepository Subjects { get; }
    ICourseRepository Courses { get; }
    IResourceRepository Resources { get; }
    IResourceShareRequestRepository ResourceShareRequests { get; }
    IStudyBuddyProfileRepository StudyBuddyProfiles { get; }
    IStudyBuddyMatchRepository StudyBuddyMatches { get; }
    IStudyGroupRepository StudyGroups { get; }
    IReviewRepository Reviews { get; }
    IMessageRepository Messages { get; }
    INotificationRepository Notifications { get; }
    IRefreshTokenRepository RefreshTokens { get; }

    Task<int> SaveAsync(CancellationToken cancellationToken = default);
}
