using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data.Repositories;

public class UserRepository(AppDbContext context)
    : RepositoryBase<AppUser>(context), IUserRepository
{
    public IQueryable<AppUser> Search(string term, bool trackChanges)
    {
        var query = FindAll(trackChanges);

        if (string.IsNullOrWhiteSpace(term))
            return query.OrderBy(u => u.FirstName).ThenBy(u => u.LastName);

        var trimmed = term.Trim();
        var pattern = $"%{trimmed}%";

        // Substring match handles "ada" inside "Adaobi" and partial usernames.
        // The concatenated comparison is what makes "ada obi" find someone whose
        // first and last names are stored in separate columns.
        //
        // Like, not ILike (Npgsql-only); SQL Server's default collation is
        // case-insensitive. Misspelled names no longer match — that relied on
        // Postgres trigram similarity, which has no SQL Server equivalent.
        query = query.Where(u =>
            EF.Functions.Like(u.UserName, pattern) ||
            EF.Functions.Like(u.FirstName, pattern) ||
            EF.Functions.Like(u.LastName, pattern) ||
            EF.Functions.Like(u.FirstName + " " + u.LastName, pattern));

        // Prefix matches first — someone typing "ada" almost always wants
        // Adaobi rather than a mid-string match on another name.
        return query
            .OrderByDescending(u =>
                EF.Functions.Like(u.FirstName, $"{trimmed}%") ||
                EF.Functions.Like(u.UserName, $"{trimmed}%"))
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
