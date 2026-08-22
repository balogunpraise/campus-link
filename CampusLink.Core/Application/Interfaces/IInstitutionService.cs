using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Application.Dtos.Institutions;

namespace CampusLink.Core.Application.Interfaces;

// Everything here is reachable before sign-in: a student has to be able to find
// their school before they have an account.
public interface IInstitutionService
{
    Task<PagedResult<InstitutionSummary>> SearchAsync(
        InstitutionSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CountrySummary>> GetCountriesAsync(CancellationToken cancellationToken = default);

    Task<Result<InstitutionDetail>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    // Maps an email address to a school via its domain, so the sign-up form can
    // skip the picker entirely for most students.
    Task<InstitutionResolveResponse> ResolveByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    // "I can't find my school." Creates it unverified and pending moderation.
    Task<Result<InstitutionSummary>> ProposeAsync(
        ProposeInstitutionRequest request,
        CancellationToken cancellationToken = default);
}
