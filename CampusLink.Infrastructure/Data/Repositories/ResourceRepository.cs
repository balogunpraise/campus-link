using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data.Repositories;

public class ResourceRepository(AppDbContext context)
    : RepositoryBase<Resource>(context), IResourceRepository
{
    public IQueryable<Resource> GetVisibleTo(
        Guid viewerInstitutionId,
        IReadOnlyCollection<Guid> partnerInstitutionIds,
        bool trackChanges) =>
        ApplyVisibility(FindAll(trackChanges), viewerInstitutionId, partnerInstitutionIds)
            .OrderByDescending(r => r.CreatedAt);

    public IQueryable<Resource> GetForCourse(Guid courseId, bool onlyAvailable, bool trackChanges)
    {
        var query = FindByCondition(r => r.CourseId == courseId, trackChanges);

        if (onlyAvailable)
            query = query.Where(r => r.IsAvailable);

        return query.OrderByDescending(r => r.CreatedAt);
    }

    public IQueryable<Resource> GetBySubjectAcrossInstitutions(
        Guid subjectId,
        Guid viewerInstitutionId,
        IReadOnlyCollection<Guid> partnerInstitutionIds,
        bool trackChanges)
    {
        var query = FindByCondition(
            r => r.SubjectId == subjectId &&
                 r.IsAvailable &&
                 r.InstitutionId != viewerInstitutionId,
            trackChanges);

        // Physical items can't be lent to another campus, so they're never part
        // of a cross-institution result even if the owner marked them Global.
        query = query.Where(r =>
            r.Type != ResourceType.PhysicalBook &&
            r.Type != ResourceType.PhysicalNotes &&
            r.Type != ResourceType.PhysicalEquipment);

        return ApplyVisibility(query, viewerInstitutionId, partnerInstitutionIds)
            .OrderByDescending(r => r.CreatedAt);
    }

    public IQueryable<Resource> GetOwnedBy(Guid ownerId, bool trackChanges) =>
        FindByCondition(r => r.OwnerId == ownerId, trackChanges)
            .OrderByDescending(r => r.CreatedAt);

    public Task<Resource> GetWithOwnerAsync(
        Guid resourceId,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(r => r.Id == resourceId, trackChanges)
            .Include(r => r.Owner)
            .Include(r => r.Institution)
            .FirstOrDefaultAsync(cancellationToken);

    // The one place the sharing rules live: own school always, partner schools
    // when the listing opted into PartnerInstitutions or Global, anyone else
    // only for Global. Kept as an expression over the query so it composes with
    // the caller's own filters and still runs in Postgres.
    private static IQueryable<Resource> ApplyVisibility(
        IQueryable<Resource> query,
        Guid viewerInstitutionId,
        IReadOnlyCollection<Guid> partnerInstitutionIds)
    {
        var partnerIds = partnerInstitutionIds ?? Array.Empty<Guid>();

        return query.Where(r =>
            r.InstitutionId == viewerInstitutionId ||
            r.Visibility == VisibilityScope.Global ||
            (r.Visibility == VisibilityScope.PartnerInstitutions && partnerIds.Contains(r.InstitutionId)));
    }
}

public class ResourceShareRequestRepository(AppDbContext context)
    : RepositoryBase<ResourceShareRequest>(context), IResourceShareRequestRepository
{
    public IQueryable<ResourceShareRequest> GetForRequester(
        Guid requesterId,
        ShareRequestStatus? status,
        bool trackChanges)
    {
        var query = FindByCondition(sr => sr.RequesterId == requesterId, trackChanges);

        if (status.HasValue)
            query = query.Where(sr => sr.Status == status.Value);

        return query.OrderByDescending(sr => sr.RequestedAt);
    }

    public IQueryable<ResourceShareRequest> GetForOwner(
        Guid ownerId,
        ShareRequestStatus? status,
        bool trackChanges)
    {
        var query = FindByCondition(sr => sr.Resource.OwnerId == ownerId, trackChanges);

        if (status.HasValue)
            query = query.Where(sr => sr.Status == status.Value);

        return query.OrderByDescending(sr => sr.RequestedAt);
    }

    public Task<ResourceShareRequest> GetActiveRequestAsync(
        Guid resourceId,
        Guid requesterId,
        CancellationToken cancellationToken = default) =>
        // "Active" = still in flight, so the caller can block a duplicate
        // request while an earlier one is pending or on loan.
        FindByCondition(
                sr => sr.ResourceId == resourceId &&
                      sr.RequesterId == requesterId &&
                      (sr.Status == ShareRequestStatus.Pending ||
                       sr.Status == ShareRequestStatus.Approved ||
                       sr.Status == ShareRequestStatus.Overdue),
                false)
            .FirstOrDefaultAsync(cancellationToken);

    public IQueryable<ResourceShareRequest> GetOverdue(DateTime asOfUtc, bool trackChanges) =>
        // Only a loan that actually started can be overdue: OnLoan means both
        // parties confirmed the handover. AwaitingReturn counts too — one side
        // says it came back but the other hasn't agreed, so the item is still
        // formally out.
        FindByCondition(
                sr => (sr.Status == ShareRequestStatus.OnLoan ||
                       sr.Status == ShareRequestStatus.AwaitingReturn) &&
                      sr.DueDate != null &&
                      sr.DueDate < asOfUtc &&
                      sr.ReturnedAt == null,
                trackChanges)
            .OrderBy(sr => sr.DueDate);

    public IQueryable<ResourceShareRequest> GetAwaitingCounterpartyConfirmation(
        DateTime confirmedBeforeUtc,
        bool trackChanges) =>
        FindByCondition(
                sr => (sr.Status == ShareRequestStatus.AwaitingHandover &&
                       (sr.OwnerConfirmedHandoverAt < confirmedBeforeUtc ||
                        sr.BorrowerConfirmedReceiptAt < confirmedBeforeUtc)) ||
                      (sr.Status == ShareRequestStatus.AwaitingReturn &&
                       (sr.BorrowerConfirmedReturnAt < confirmedBeforeUtc ||
                        sr.OwnerConfirmedReturnAt < confirmedBeforeUtc)),
                trackChanges)
            .OrderBy(sr => sr.RequestedAt);
}
