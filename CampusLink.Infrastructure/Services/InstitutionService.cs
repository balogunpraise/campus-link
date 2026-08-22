using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CampusLink.Core.Application.Common;
using CampusLink.Core.Application.Dtos.Common;
using CampusLink.Core.Application.Dtos.Institutions;
using CampusLink.Core.Application.Interfaces;
using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusLink.Infrastructure.Services;

public class InstitutionService(
    IRepositoryManager repository,
    ILogger<InstitutionService> logger) : IInstitutionService
{
    public async Task<PagedResult<InstitutionSummary>> SearchAsync(
        InstitutionSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = repository.Institutions.Search(
            request.Search, request.CountryCode, request.VerifiedOnly, false);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(i => new InstitutionSummary
            {
                Id = i.Id,
                Name = i.Name,
                Slug = i.Slug,
                ShortName = i.ShortName,
                Type = i.Type.ToString(),
                CountryCode = i.CountryCode,
                State = i.State,
                City = i.City,
                LogoUrl = i.LogoUrl,
                IsVerified = i.IsVerified
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<InstitutionSummary>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = total
        };
    }

    public async Task<IReadOnlyList<CountrySummary>> GetCountriesAsync(
        CancellationToken cancellationToken = default)
    {
        var counts = await repository.Institutions.GetCountryCountsAsync(cancellationToken);

        return counts
            .Select(c => new CountrySummary { CountryCode = c.CountryCode, InstitutionCount = c.Count })
            .ToList();
    }

    public async Task<Result<InstitutionDetail>> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var institution = await repository.Institutions
            .FindByCondition(i => i.Slug == slug.ToLower(), false)
            .Include(i => i.EmailDomains)
            .Select(i => new InstitutionDetail
            {
                Id = i.Id,
                Name = i.Name,
                Slug = i.Slug,
                ShortName = i.ShortName,
                Type = i.Type.ToString(),
                CountryCode = i.CountryCode,
                State = i.State,
                City = i.City,
                LogoUrl = i.LogoUrl,
                IsVerified = i.IsVerified,
                WebsiteUrl = i.WebsiteUrl,
                TimeZoneId = i.TimeZoneId,
                AllowsCrossInstitutionSharing = i.AllowsCrossInstitutionSharing,
                EmailDomains = i.EmailDomains.Select(d => d.Domain).ToList(),
                MemberCount = i.Members.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        return institution is null
            ? Result<InstitutionDetail>.Failure(AuthErrorCode.NotFound, "Institution not found.")
            : Result<InstitutionDetail>.Success(institution);
    }

    public async Task<InstitutionResolveResponse> ResolveByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var at = email?.IndexOf('@') ?? -1;

        if (at < 0 || at == email.Length - 1)
            return new InstitutionResolveResponse
            {
                Resolved = false,
                Message = "Enter a full email address."
            };

        var domain = email[(at + 1)..].Trim().ToLowerInvariant();
        var institution = await repository.Institutions.GetByEmailDomainAsync(domain, cancellationToken);

        if (institution is null)
            return new InstitutionResolveResponse
            {
                Resolved = false,
                Domain = domain,
                Message = "We don't recognise that email domain. Search for your institution instead."
            };

        return new InstitutionResolveResponse
        {
            Resolved = true,
            Domain = domain,
            Institution = ToSummary(institution)
        };
    }

    public async Task<Result<InstitutionSummary>> ProposeAsync(
        ProposeInstitutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        var countryCode = request.CountryCode.Trim().ToUpperInvariant();

        if (!IsKnownCountryCode(countryCode))
            return Result<InstitutionSummary>.Failure(
                AuthErrorCode.ValidationFailed, "Country must be a valid ISO 3166-1 alpha-2 code.");

        // Near-identical name in the same country almost always means the school
        // is already here under a slightly different spelling.
        if (await repository.Institutions.SimilarNameExistsAsync(name, countryCode, cancellationToken))
            return Result<InstitutionSummary>.Failure(
                AuthErrorCode.Conflict,
                "An institution with a very similar name already exists in that country. Search for it instead.");

        var domain = NormalizeDomain(request.EmailDomain);

        // A domain is what grants verified membership, so it can only ever
        // belong to one school — first claim wins.
        if (domain is not null &&
            await repository.Institutions.GetByEmailDomainAsync(domain, cancellationToken) is not null)
            return Result<InstitutionSummary>.Failure(
                AuthErrorCode.Conflict, "That email domain is already registered to another institution.");

        var institution = new Institution
        {
            Name = name,
            Slug = await GenerateUniqueSlugAsync(name, countryCode, cancellationToken),
            ShortName = request.ShortName?.Trim(),
            Type = request.Type,
            CountryCode = countryCode,
            City = request.City?.Trim(),
            State = request.State?.Trim(),
            WebsiteUrl = request.WebsiteUrl?.Trim(),

            // Unverified and flagged for moderation: until an admin vets it,
            // its members stay confined to their own institution.
            IsVerified = false,
            IsUserSubmitted = true
        };

        if (domain is not null)
            institution.EmailDomains.Add(new InstitutionEmailDomain { Domain = domain, IsStudentDomain = true });

        await repository.Institutions.CreateAsync(institution, cancellationToken);
        await repository.SaveAsync(cancellationToken);

        logger.LogInformation(
            "Institution {InstitutionId} proposed by a user: {Name} ({CountryCode})",
            institution.Id, institution.Name, countryCode);

        return Result<InstitutionSummary>.Success(ToSummary(institution));
    }

    // ----- helpers -----

    private static InstitutionSummary ToSummary(Institution i) => new()
    {
        Id = i.Id,
        Name = i.Name,
        Slug = i.Slug,
        ShortName = i.ShortName,
        Type = i.Type.ToString(),
        CountryCode = i.CountryCode,
        State = i.State,
        City = i.City,
        LogoUrl = i.LogoUrl,
        IsVerified = i.IsVerified
    };

    private async Task<string> GenerateUniqueSlugAsync(
        string name,
        string countryCode,
        CancellationToken cancellationToken)
    {
        var baseSlug = Slugify(name);

        if (string.IsNullOrEmpty(baseSlug))
            baseSlug = "institution";

        // Same-named schools exist in different countries ("american
        // university"), so the country code is the natural first tiebreaker.
        var candidate = baseSlug;

        if (await repository.Institutions.SlugExistsAsync(candidate, cancellationToken))
            candidate = $"{baseSlug}-{countryCode.ToLowerInvariant()}";

        var suffix = 2;
        while (await repository.Institutions.SlugExistsAsync(candidate, cancellationToken))
            candidate = $"{baseSlug}-{countryCode.ToLowerInvariant()}-{suffix++}";

        return candidate;
    }

    // Institution names arrive in every script and with every accent, so strip
    // diacritics first and fall back to a generic slug when nothing ASCII is
    // left (a Chinese or Arabic name slugifies to empty otherwise).
    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        var slug = builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();

        slug = Regex.Replace(slug, "[^a-z0-9]+", "-");
        slug = slug.Trim('-');

        return slug.Length > 80 ? slug[..80].TrimEnd('-') : slug;
    }

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        var value = domain.Trim().ToLowerInvariant().TrimStart('@');

        // Strip a pasted URL down to its host.
        value = Regex.Replace(value, "^https?://", string.Empty);
        value = value.Split('/')[0];

        return value.Contains('.') ? value : null;
    }

    private static bool IsKnownCountryCode(string code) =>
        code.Length == 2 &&
        code.All(char.IsAsciiLetterUpper) &&
        // RegionInfo throws for codes that aren't real ISO regions.
        TryGetRegion(code);

    private static bool TryGetRegion(string code)
    {
        try
        {
            _ = new RegionInfo(code);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
