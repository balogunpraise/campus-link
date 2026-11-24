using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data.Repositories;

public class UserRepository(AppDbContext context)
    : RepositoryBase<AppUser>(context), IUserRepository
{
    // Low enough that "chinwe" still reaches "Chinweike", high enough to keep
    // the whole directory out of the result set.
    private const double SimilarityThreshold = 0.3;

    public IQueryable<AppUser> Search(string term, bool trackChanges)
    {
        var query = FindAll(trackChanges);

        if (string.IsNullOrWhiteSpace(term))
            return query.OrderBy(u => u.FirstName).ThenBy(u => u.LastName);

        var trimmed = term.Trim();
        var pattern = $"%{trimmed}%";

        // Substring match handles "ada" inside "Adaobi" and partial usernames;
        // trigram similarity handles misspelled names. The full-name comparison
        // is what makes "ada obi" find someone whose first and last names are
        // stored separately.
        query = query.Where(u =>
            EF.Functions.ILike(u.UserName, pattern) ||
            EF.Functions.ILike(u.FirstName, pattern) ||
            EF.Functions.ILike(u.LastName, pattern) ||
            EF.Functions.ILike(u.FirstName + " " + u.LastName, pattern) ||
            EF.Functions.TrigramsSimilarity(u.FirstName + " " + u.LastName, trimmed) > SimilarityThreshold);

        // Prefix matches first — someone typing "ada" almost always wants
        // Adaobi, not a fuzzy match on a different name.
        return query
            .OrderByDescending(u =>
                EF.Functions.ILike(u.FirstName, $"{trimmed}%") ||
                EF.Functions.ILike(u.UserName, $"{trimmed}%"))
            .ThenByDescending(u => EF.Functions.TrigramsSimilarity(u.FirstName + " " + u.LastName, trimmed))
            .ThenBy(u => u.FirstName)
            .ThenBy(u => u.LastName);
    }

    public Task<AppUser> GetWithInstitutionAsync(
        Guid userId,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(u => u.Id == userId, trackChanges)
            .Include(u => u.Institution)
            .FirstOrDefaultAsync(cancellationToken);
}
