using System.Text.Json;
using System.Text.Json.Serialization;
using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Enums;
using CampusLink.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusLink.Infrastructure.Data.Seed;

// Imports the Hipolabs world universities dataset (name, country, alpha-2 code,
// domains) so email-domain sign-up works on day one for most of the world
// instead of only for schools someone typed in by hand.
//
// Source: https://github.com/Hipo/university-domains-list (CC-licensed,
// community-maintained). Rows land as IsVerified = false: the list is useful,
// not authoritative, so an admin still vets a school before its members get
// cross-institution reach.
public class InstitutionSeeder(
    AppDbContext context,
    ILogger<InstitutionSeeder> logger)
{
    private const string DataFileName = "world_universities_and_domains.json";

    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await context.Institutions.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Institutions already seeded; skipping.");
            return 0;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", DataFileName);

        if (!File.Exists(path))
        {
            logger.LogWarning("Institution seed file not found at {Path}; skipping seed.", path);
            return 0;
        }

        await using var stream = File.OpenRead(path);
        var records = await JsonSerializer.DeserializeAsync<List<SeedRecord>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken) ?? [];

        var institutions = new List<Institution>(records.Count);

        // Both are globally unique in the schema, so dedupe in memory rather
        // than letting ~22k inserts fail one at a time on the database.
        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Name) || string.IsNullOrWhiteSpace(record.AlphaTwoCode))
                continue;

            var countryCode = record.AlphaTwoCode.Trim().ToUpperInvariant();
            var slug = MakeUniqueSlug(record.Name, countryCode, usedSlugs);

            var institution = new Institution
            {
                Name = record.Name.Trim(),
                Slug = slug,
                Type = InferType(record.Name),
                CountryCode = countryCode,
                State = string.IsNullOrWhiteSpace(record.StateProvince) ? null : record.StateProvince.Trim(),
                WebsiteUrl = record.WebPages?.FirstOrDefault(),
                IsVerified = false,
                IsUserSubmitted = false
            };

            foreach (var domain in record.Domains ?? [])
            {
                var normalized = domain?.Trim().ToLowerInvariant();

                // A domain decides which school an account belongs to, so a
                // duplicate across two rows would be a verification hole.
                // First institution listed keeps it.
                if (string.IsNullOrWhiteSpace(normalized) || !usedDomains.Add(normalized))
                    continue;

                institution.EmailDomains.Add(new InstitutionEmailDomain
                {
                    Domain = normalized,
                    IsStudentDomain = true
                });
            }

            institutions.Add(institution);
        }

        // One SaveChanges for ~10k institutions and their domains. Change
        // tracking is the bottleneck at this size, so turn it off for the load.
        var autoDetect = context.ChangeTracker.AutoDetectChangesEnabled;
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            await context.Institutions.AddRangeAsync(institutions, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }

        logger.LogInformation(
            "Seeded {InstitutionCount} institutions and {DomainCount} email domains from {File}.",
            institutions.Count, usedDomains.Count, DataFileName);

        return institutions.Count;
    }

    private static string MakeUniqueSlug(string name, string countryCode, HashSet<string> used)
    {
        var baseSlug = InstitutionService.Slugify(name);

        // Names in non-Latin scripts slugify to nothing.
        if (string.IsNullOrEmpty(baseSlug))
            baseSlug = $"institution-{countryCode.ToLowerInvariant()}";

        if (used.Add(baseSlug))
            return baseSlug;

        var withCountry = $"{baseSlug}-{countryCode.ToLowerInvariant()}";
        if (used.Add(withCountry))
            return withCountry;

        var suffix = 2;
        while (!used.Add($"{withCountry}-{suffix}"))
            suffix++;

        return $"{withCountry}-{suffix}";
    }

    // The dataset has no type field; the name is the only signal available.
    // Anything unrecognised stays University, which is true for most rows.
    private static InstitutionType InferType(string name)
    {
        var lower = name.ToLowerInvariant();

        if (lower.Contains("polytechnic")) return InstitutionType.Polytechnic;
        if (lower.Contains("college")) return InstitutionType.College;
        if (lower.Contains("school") && !lower.Contains("school of")) return InstitutionType.HighSchool;

        return InstitutionType.University;
    }

    private class SeedRecord
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("alpha_two_code")]
        public string AlphaTwoCode { get; set; }

        [JsonPropertyName("domains")]
        public List<string> Domains { get; set; }

        [JsonPropertyName("web_pages")]
        public List<string> WebPages { get; set; }

        [JsonPropertyName("state-province")]
        public string StateProvince { get; set; }
    }
}
