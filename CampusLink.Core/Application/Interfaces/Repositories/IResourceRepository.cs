using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Application.Interfaces.Repositories;

public interface IResourceRepository : IRepositoryBase<Resource>
{
    // The visibility gate for browsing. Returns resources the viewer is allowed
    // to see: their own school's, partner schools' where the listing allows it,
    // and anything marked Global. Callers pass the partner ids from
    // IInstitutionPartnershipRepository so this stays a single query.
    IQueryable<Resource> GetVisibleTo(
        Guid viewerInstitutionId,
        IReadOnlyCollection<Guid> partnerInstitutionIds,
        bool trackChanges);

    IQueryable<Resource> GetForCourse(Guid courseId, bool onlyAvailable, bool trackChanges);

    // Cross-school discovery: same subject, any institution, filtered to what
    // the viewer may see. Physical items are excluded because they can't be
    // handed over to another campus.
    IQueryable<Resource> GetBySubjectAcrossInstitutions(
        Guid subjectId,
        Guid viewerInstitutionId,
        IReadOnlyCollection<Guid> partnerInstitutionIds,
        bool trackChanges);

    IQueryable<Resource> GetOwnedBy(Guid ownerId, bool trackChanges);

    Task<Resource> GetWithOwnerAsync(Guid resourceId, bool trackChanges, CancellationToken cancellationToken = default);
}

public interface IResourceShareRequestRepository : IRepositoryBase<ResourceShareRequest>
{
    IQueryable<ResourceShareRequest> GetForRequester(Guid requesterId, ShareRequestStatus? status, bool trackChanges);

    // The owner's inbox — requests against anything they listed.
    IQueryable<ResourceShareRequest> GetForOwner(Guid ownerId, ShareRequestStatus? status, bool trackChanges);

    Task<ResourceShareRequest> GetActiveRequestAsync(
        Guid resourceId,
        Guid requesterId,
        CancellationToken cancellationToken = default);

    // Physical loans past their due date, for the overdue sweep.
    IQueryable<ResourceShareRequest> GetOverdue(DateTime asOfUtc, bool trackChanges);

    // Loans where one party confirmed a handover or return and the other hasn't
    // yet, and enough time has passed that it's no longer just a delay. Drives
    // the reminder to the silent party, then the dispute flag.
    IQueryable<ResourceShareRequest> GetAwaitingCounterpartyConfirmation(
        DateTime confirmedBeforeUtc,
        bool trackChanges);
}
