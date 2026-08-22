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

    // Similarity floor for fuzzy matching. Low enough that "massachusets"
    // still reaches "Massachusetts", high enough to keep noise out.
    private const double SimilarityThreshold = 0.25;

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

        // Three different ways a student might type their school:
        //   ILIKE on name/short name/slug — "lagos" inside "University of Lagos"
        //   ILIKE on email domain — "unilag" only appears in unilag.edu.ng, and
        //     acronyms are what students actually type; trigram can't bridge
        //     "unilag" to "University of Lagos" because they share no trigrams
        //   trigram similarity on name — misspellings ("massachusets")
        query = query.Where(i =>
            EF.Functions.ILike(i.Name, pattern) ||
            EF.Functions.ILike(i.ShortName, pattern) ||
            EF.Functions.ILike(i.Slug, pattern) ||
            i.EmailDomains.Any(d => EF.Functions.ILike(d.Domain, pattern)) ||
            EF.Functions.TrigramsSimilarity(i.Name, trimmed) > SimilarityThreshold);

        // Rank: exact-ish prefix first, then closeness, then verified schools,
        // so the real "University of Lagos" outranks a student's typo'd copy.
        // A domain that starts with the term ranks like a name prefix — that's
        // the acronym case, and it's usually exactly what was meant.
        return query
            .OrderByDescending(i =>
                EF.Functions.ILike(i.Name, $"{trimmed}%") ||
                i.EmailDomains.Any(d => EF.Functions.ILike(d.Domain, $"{trimmed}%")))
            .ThenByDescending(i => EF.Functions.TrigramsSimilarity(i.Name, trimmed))
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
        // created, so it should only fire on a near-identical name.
        return ExistsAsync(
            i => i.CountryCode == code &&
                 EF.Functions.TrigramsSimilarity(i.Name, trimmed) > 0.7,
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
