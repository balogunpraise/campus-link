using CampusLink.Core.Domain.Entities;
using CampusLink.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampusLink.Infrastructure.Data.Seed;

// Seeds the shared subject taxonomy. Unlike institutions there's no public
// dataset worth importing, so this is a deliberately small curated starter set:
// broad topics that exist at most universities, which is exactly what makes
// them usable for cross-institution matching. Students can propose more, and
// those stay unapproved until moderated.
public class SubjectSeeder(AppDbContext context, ILogger<SubjectSeeder> logger)
{
    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await context.Subjects
            .Select(s => s.Slug)
            .ToListAsync(cancellationToken);

        var known = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = Catalogue
            .Select(entry => new Subject
            {
                Name = entry.Name,
                Slug = InstitutionService.Slugify(entry.Name),
                Field = entry.Field,
                IsApproved = true
            })
            .Where(s => known.Add(s.Slug))
            .ToList();

        if (toAdd.Count == 0)
        {
            logger.LogInformation("Subject taxonomy already seeded; skipping.");
            return 0;
        }

        await context.Subjects.AddRangeAsync(toAdd, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded {Count} subjects.", toAdd.Count);

        return toAdd.Count;
    }

    private static readonly (string Name, string Field)[] Catalogue =
    [
        // Computer Science
        ("Programming Fundamentals", "Computer Science"),
        ("Data Structures", "Computer Science"),
        ("Algorithms", "Computer Science"),
        ("Operating Systems", "Computer Science"),
        ("Computer Networks", "Computer Science"),
        ("Databases", "Computer Science"),
        ("Software Engineering", "Computer Science"),
        ("Web Development", "Computer Science"),
        ("Machine Learning", "Computer Science"),
        ("Artificial Intelligence", "Computer Science"),
        ("Cybersecurity", "Computer Science"),
        ("Computer Architecture", "Computer Science"),

        // Mathematics
        ("Calculus", "Mathematics"),
        ("Linear Algebra", "Mathematics"),
        ("Discrete Mathematics", "Mathematics"),
        ("Probability", "Mathematics"),
        ("Statistics", "Mathematics"),
        ("Differential Equations", "Mathematics"),
        ("Real Analysis", "Mathematics"),
        ("Numerical Methods", "Mathematics"),

        // Physical sciences
        ("Classical Mechanics", "Physics"),
        ("Electromagnetism", "Physics"),
        ("Thermodynamics", "Physics"),
        ("Quantum Mechanics", "Physics"),
        ("Organic Chemistry", "Chemistry"),
        ("Inorganic Chemistry", "Chemistry"),
        ("Physical Chemistry", "Chemistry"),
        ("Analytical Chemistry", "Chemistry"),

        // Life sciences and medicine
        ("Cell Biology", "Biology"),
        ("Genetics", "Biology"),
        ("Microbiology", "Biology"),
        ("Ecology", "Biology"),
        ("Human Anatomy", "Medicine"),
        ("Physiology", "Medicine"),
        ("Biochemistry", "Medicine"),
        ("Pharmacology", "Medicine"),
        ("Pathology", "Medicine"),
        ("Public Health", "Medicine"),
        ("Nursing Practice", "Medicine"),

        // Engineering
        ("Engineering Mathematics", "Engineering"),
        ("Circuit Analysis", "Engineering"),
        ("Signals and Systems", "Engineering"),
        ("Control Systems", "Engineering"),
        ("Fluid Mechanics", "Engineering"),
        ("Strength of Materials", "Engineering"),
        ("Structural Analysis", "Engineering"),
        ("Chemical Process Engineering", "Engineering"),

        // Business, law, social sciences
        ("Microeconomics", "Economics"),
        ("Macroeconomics", "Economics"),
        ("Econometrics", "Economics"),
        ("Financial Accounting", "Business"),
        ("Management Accounting", "Business"),
        ("Corporate Finance", "Business"),
        ("Marketing", "Business"),
        ("Business Law", "Business"),
        ("Constitutional Law", "Law"),
        ("Contract Law", "Law"),
        ("Criminal Law", "Law"),
        ("Psychology", "Social Sciences"),
        ("Sociology", "Social Sciences"),
        ("Political Science", "Social Sciences"),
        ("International Relations", "Social Sciences"),

        // Humanities and languages
        ("Academic Writing", "Humanities"),
        ("Philosophy", "Humanities"),
        ("History", "Humanities"),
        ("English Literature", "Humanities"),
        ("French", "Languages"),
        ("Spanish", "Languages"),
        ("Mandarin Chinese", "Languages"),
        ("Arabic", "Languages"),

        // Exams students commonly study together for
        ("IELTS Preparation", "Test Preparation"),
        ("TOEFL Preparation", "Test Preparation"),
        ("GRE Preparation", "Test Preparation"),
        ("GMAT Preparation", "Test Preparation")
    ];
}
