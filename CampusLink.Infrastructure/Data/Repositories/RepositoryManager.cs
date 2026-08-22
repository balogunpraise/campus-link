using CampusLink.Core.Application.Interfaces.Repositories;

namespace CampusLink.Infrastructure.Data.Repositories;

// All repositories share this instance's AppDbContext, so anything staged
// across them commits together in one SaveAsync. Lazy<T> means a request only
// constructs the repositories it actually touches.
public class RepositoryManager : IRepositoryManager
{
    private readonly AppDbContext _context;

    private readonly Lazy<IInstitutionRepository> _institutions;
    private readonly Lazy<IInstitutionEmailDomainRepository> _institutionEmailDomains;
    private readonly Lazy<IInstitutionPartnershipRepository> _institutionPartnerships;
    private readonly Lazy<ISubjectRepository> _subjects;
    private readonly Lazy<ICourseRepository> _courses;
    private readonly Lazy<IResourceRepository> _resources;
    private readonly Lazy<IResourceShareRequestRepository> _resourceShareRequests;
    private readonly Lazy<IStudyBuddyProfileRepository> _studyBuddyProfiles;
    private readonly Lazy<IStudyBuddyMatchRepository> _studyBuddyMatches;
    private readonly Lazy<IStudyGroupRepository> _studyGroups;
    private readonly Lazy<IReviewRepository> _reviews;
    private readonly Lazy<IMessageRepository> _messages;
    private readonly Lazy<INotificationRepository> _notifications;
    private readonly Lazy<IRefreshTokenRepository> _refreshTokens;

    public RepositoryManager(AppDbContext context)
    {
        _context = context;

        _institutions = new Lazy<IInstitutionRepository>(() => new InstitutionRepository(context));
        _institutionEmailDomains = new Lazy<IInstitutionEmailDomainRepository>(
            () => new InstitutionEmailDomainRepository(context));
        _institutionPartnerships = new Lazy<IInstitutionPartnershipRepository>(
            () => new InstitutionPartnershipRepository(context));
        _subjects = new Lazy<ISubjectRepository>(() => new SubjectRepository(context));
        _courses = new Lazy<ICourseRepository>(() => new CourseRepository(context));
        _resources = new Lazy<IResourceRepository>(() => new ResourceRepository(context));
        _resourceShareRequests = new Lazy<IResourceShareRequestRepository>(
            () => new ResourceShareRequestRepository(context));
        _studyBuddyProfiles = new Lazy<IStudyBuddyProfileRepository>(() => new StudyBuddyProfileRepository(context));
        _studyBuddyMatches = new Lazy<IStudyBuddyMatchRepository>(() => new StudyBuddyMatchRepository(context));
        _studyGroups = new Lazy<IStudyGroupRepository>(() => new StudyGroupRepository(context));
        _reviews = new Lazy<IReviewRepository>(() => new ReviewRepository(context));
        _messages = new Lazy<IMessageRepository>(() => new MessageRepository(context));
        _notifications = new Lazy<INotificationRepository>(() => new NotificationRepository(context));
        _refreshTokens = new Lazy<IRefreshTokenRepository>(() => new RefreshTokenRepository(context));
    }

    public IInstitutionRepository Institutions => _institutions.Value;
    public IInstitutionEmailDomainRepository InstitutionEmailDomains => _institutionEmailDomains.Value;
    public IInstitutionPartnershipRepository InstitutionPartnerships => _institutionPartnerships.Value;
    public ISubjectRepository Subjects => _subjects.Value;
    public ICourseRepository Courses => _courses.Value;
    public IResourceRepository Resources => _resources.Value;
    public IResourceShareRequestRepository ResourceShareRequests => _resourceShareRequests.Value;
    public IStudyBuddyProfileRepository StudyBuddyProfiles => _studyBuddyProfiles.Value;
    public IStudyBuddyMatchRepository StudyBuddyMatches => _studyBuddyMatches.Value;
    public IStudyGroupRepository StudyGroups => _studyGroups.Value;
    public IReviewRepository Reviews => _reviews.Value;
    public IMessageRepository Messages => _messages.Value;
    public INotificationRepository Notifications => _notifications.Value;
    public IRefreshTokenRepository RefreshTokens => _refreshTokens.Value;

    public Task<int> SaveAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
