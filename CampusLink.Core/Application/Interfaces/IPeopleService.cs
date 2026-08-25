using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Application.Dtos.People;

namespace CampusLink.Core.Application.Interfaces;

// Finding a specific person by name, as opposed to being matched with someone
// by shared interest. Both end in the same study-buddy request.
public interface IPeopleService
{
    Task<Result<PagedResult<PersonSummary>>> SearchAsync(
        Guid userId,
        PeopleSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<PersonSummary>> GetAsync(
        Guid userId,
        Guid targetUserId,
        CancellationToken cancellationToken = default);
}
