using CampusLink.Core.Domain.Entities;

namespace CampusLink.Core.Application.Interfaces.Repositories;

// Read-side access to students. Identity's UserManager still owns creation,
// passwords and roles — this is only for discovery queries, which UserManager
// has no vocabulary for.
public interface IUserRepository : IRepositoryBase<AppUser>
{
    // Directory search, typo-tolerant across username, first and last name.
    // Visibility is applied by the caller, not here.
    IQueryable<AppUser> Search(string term, bool trackChanges);

    Task<AppUser> GetWithInstitutionAsync(
        Guid userId,
        bool trackChanges,
        CancellationToken cancellationToken = default);
}
