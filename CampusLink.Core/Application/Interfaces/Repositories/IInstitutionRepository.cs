using CampusLink.Core.Domain.Entities;

namespace CampusLink.Core.Application.Interfaces.Repositories;

public interface IInstitutionRepository : IRepositoryBase<Institution>
{
    Task<Institution> GetBySlugAsync(string slug, bool trackChanges, CancellationToken cancellationToken = default);

    // Sign-up path: resolve "ada@student.unilag.edu.ng" to the school that owns
    // that domain. Returns null when no school claims it, which is what leaves
    // the new member Unverified.
    Task<Institution> GetByEmailDomainAsync(string emailDomain, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    // Directory search used when a student picks their school at sign-up.
    // All filters are optional (null/empty = ignored). Typo-tolerant: ranked by
    // trigram similarity, so "unilag" finds "University of Lagos".
    IQueryable<Institution> Search(string term, string countryCode, bool verifiedOnly, bool trackChanges);

    // Country codes that actually have institutions, with counts — drives the
    // country selector without shipping a hardcoded ISO list.
    Task<IReadOnlyList<(string CountryCode, int Count)>> GetCountryCountsAsync(
        CancellationToken cancellationToken = default);

    // Guards against a student proposing a school that already exists under a
    // slightly different name.
    Task<bool> SimilarNameExistsAsync(
        string name,
        string countryCode,
        CancellationToken cancellationToken = default);
}

public interface IInstitutionPartnershipRepository : IRepositoryBase<InstitutionPartnership>
{
    // The institutions an active partnership connects to this one. Callers feed
    // this into the PartnerInstitutions visibility checks, so it excludes
    // Pending/Ended rows and does not include the institution itself.
    Task<IReadOnlyList<Guid>> GetPartnerInstitutionIdsAsync(
        Guid institutionId,
        CancellationToken cancellationToken = default);

    Task<InstitutionPartnership> GetBetweenAsync(
        Guid institutionAId,
        Guid institutionBId,
        bool trackChanges,
        CancellationToken cancellationToken = default);
}

public interface IInstitutionEmailDomainRepository : IRepositoryBase<InstitutionEmailDomain>
{
    Task<InstitutionEmailDomain> GetByDomainAsync(string domain, CancellationToken cancellationToken = default);

    IQueryable<InstitutionEmailDomain> GetForInstitution(Guid institutionId, bool trackChanges);
}
