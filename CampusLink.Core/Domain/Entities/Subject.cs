namespace CampusLink.Core.Domain.Entities;

// A cross-institution academic topic ("Data Structures", "Organic Chemistry").
//
// Courses are institution-specific — UNILAG's CSC201 and MIT's 6.006 are
// different rows — so they can't be used to match students across schools.
// Subjects are the shared vocabulary that makes that possible: a course maps
// to a subject, and study-buddy matching / resource discovery run on subjects.
public class Subject : BaseEntity
{
    public string Name { get; set; }

    // Stable, url-safe identifier, e.g. "data-structures".
    public string Slug { get; set; }

    // Broad grouping, e.g. "Computer Science", "Medicine".
    public string Field { get; set; }

    public string Description { get; set; }

    // Subjects proposed by students stay unapproved until moderated, so the
    // shared taxonomy doesn't fragment into near-duplicates.
    public bool IsApproved { get; set; }

    public ICollection<Course> Courses { get; set; } = new List<Course>();
    public ICollection<Resource> Resources { get; set; } = new List<Resource>();
}
