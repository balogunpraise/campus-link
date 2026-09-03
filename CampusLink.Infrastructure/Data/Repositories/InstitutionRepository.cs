using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data.Repositories;

public class InstitutionRepository(AppDbContext context)
    : RepositoryBase<Institution>(context), IInstitutionRepository
{
    public Task<Institution> GetBySlugAsync(
        string slug,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(i => i.Slug == slug.ToLower(), trackChanges)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Institution> GetByEmailDomainAsync(
        string emailDomain,
        CancellationToken cancellationToken = default)
    {
        var normalized = emailDomain.TrimStart('@').ToLower();

        return Context.InstitutionEmailDomains
            .AsNoTracking()
            .Where(d => d.Domain == normalized)
            .Select(d => d.Institution)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) =>
        ExistsAsync(i => i.Slug == slug.ToLower(), cancellationToken);

    public IQueryable<Institution> Search(string term, string countryCode, bool verifiedOnly, bool trackChanges)
    {
        var query = FindAll(trackChanges);

        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            var code = countryCode.Trim().ToUpperInvariant();
            query = query.Where(i => i.CountryCode == code);
        }

        if (verifiedOnly)
            query = query.Where(i => i.IsVerified);

        if (string.IsNullOrWhiteSpace(term))
            return query.OrderByDescending(i => i.IsVerified).ThenBy(i => i.Name);

        var trimmed = term.Trim();
        var pattern = $"%{trimmed}%";

        // Two ways a student might type their school:
        //   LIKE on name/short name/slug — "lagos" inside "University of Lagos"
        //   LIKE on email domain — "unilag" only appears in unilag.edu.ng, and
        //     acronyms are what students actually type
        //
        // EF.Functions.Like, not ILike: ILike is Npgsql-only. SQL Server's
        // default collation is case-insensitive, so LIKE already ignores case —
        // but that depends on the database's collation, not on this code.
        //
        // The misspelling case ("massachusets" -> "Massachusetts") is gone with
        // the move off Postgres: SQL Server has no trigram similarity, so there
        // is nothing to rank fuzzy matches by.
        query = query.Where(i =>
            EF.Functions.Like(i.Name, pattern) ||
            EF.Functions.Like(i.ShortName, pattern) ||
            EF.Functions.Like(i.Slug, pattern) ||
            i.EmailDomains.Any(d => EF.Functions.Like(d.Domain, pattern)));

        // Rank: exact-ish prefix first, then closeness, then verified schools,
        // so the real "University of Lagos" outranks a student's typo'd copy.
        // A domain that starts with the term ranks like a name prefix — that's
        // the acronym case, and it's usually exactly what was meant.
        return query
            .OrderByDescending(i =>
                EF.Functions.Like(i.Name, $"{trimmed}%") ||
                i.EmailDomains.Any(d => EF.Functions.Like(d.Domain, $"{trimmed}%")))
            // Shorter names rank first among equally-good prefix matches, which
            // is a rough stand-in for the similarity ordering: "Data Structures"
            // beats "Advanced Data Structures and Algorithms" for "data".
            .ThenBy(i => i.Name.Length)
            .ThenByDescending(i => i.IsVerified)
            .ThenBy(i => i.Name);
    }

    public async Task<IReadOnlyList<(string CountryCode, int Count)>> GetCountryCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await FindAll(false)
            .GroupBy(i => i.CountryCode)
            .Select(g => new { CountryCode = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.CountryCode, r.Count)).ToList();
    }

    public Task<bool> SimilarNameExistsAsync(
        string name,
        string countryCode,
        CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        var code = countryCode?.Trim().ToUpperInvariant();

        // Deliberately stricter than search: this blocks a duplicate being
        // created, so it only fires on a near-identical name.
        //
        // On Postgres this was a 0.7 trigram-similarity threshold, which caught
        // "Univ. of Lagos" against "University of Lagos". SQL Server can only do
        // an exact (collation-insensitive) match, so close-but-not-identical
        // spellings will now slip through as new institutions — the moderation
        // queue (IsUserSubmitted) is what catches them.
        return ExistsAsync(
            i => i.CountryCode == code && i.Name == trimmed,
            cancellationToken);
    }
}

public class InstitutionEmailDomainRepository(AppDbContext context)
    : RepositoryBase<InstitutionEmailDomain>(context), IInstitutionEmailDomainRepository
{
    public Task<InstitutionEmailDomain> GetByDomainAsync(
        string domain,
        CancellationToken cancellationToken = default)
    {
        var normalized = domain.TrimStart('@').ToLower();

        return FindByCondition(d => d.Domain == normalized, false)
            .Include(d => d.Institution)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public IQueryable<InstitutionEmailDomain> GetForInstitution(Guid institutionId, bool trackChanges) =>
        FindByCondition(d => d.InstitutionId == institutionId, trackChanges)
            .OrderBy(d => d.Domain);
}

public class InstitutionPartnershipRepository(AppDbContext context)
    : RepositoryBase<InstitutionPartnership>(context), IInstitutionPartnershipRepository
{
    public async Task<IReadOnlyList<Guid>> GetPartnerInstitutionIdsAsync(
        Guid institutionId,
        CancellationToken cancellationToken = default) =>
        // Rows are stored with the smaller Guid in A, so a school can appear on
        // either side of the pair; pick whichever end isn't the caller.
        await FindByCondition(
                p => p.Status == PartnershipStatus.Active &&
                     (p.InstitutionAId == institutionId || p.InstitutionBId == institutionId),
                false)
            .Select(p => p.InstitutionAId == institutionId ? p.InstitutionBId : p.InstitutionAId)
            .ToListAsync(cancellationToken);

    public Task<InstitutionPartnership> GetBetweenAsync(
        Guid institutionAId,
        Guid institutionBId,
        bool trackChanges,
        CancellationToken cancellationToken = default)
    {
        // Normalize to the stored order so callers don't have to.
        var (first, second) = institutionAId.CompareTo(institutionBId) < 0
            ? (institutionAId, institutionBId)
            : (institutionBId, institutionAId);

        return FindByCondition(p => p.InstitutionAId == first && p.InstitutionBId == second, trackChanges)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
