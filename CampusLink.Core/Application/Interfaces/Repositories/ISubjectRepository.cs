using CampusLink.Core.Domain.Entities;

namespace CampusLink.Core.Application.Interfaces.Repositories;

public interface ISubjectRepository : IRepositoryBase<Subject>
{
    Task<Subject> GetBySlugAsync(string slug, bool trackChanges, CancellationToken cancellationToken = default);

    // Only approved subjects should be offered as choices, otherwise the shared
    // taxonomy fragments into near-duplicates and cross-school matching breaks.
    IQueryable<Subject> SearchApproved(string term, string field, bool trackChanges);

    IQueryable<Subject> GetPendingApproval(bool trackChanges);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);
}

public interface ICourseRepository : IRepositoryBase<Course>
{
    IQueryable<Course> GetForInstitution(Guid institutionId, bool trackChanges);

    // Course codes only identify a course within one school.
    Task<Course> GetByCodeAsync(
        Guid institutionId,
        string code,
        bool trackChanges,
        CancellationToken cancellationToken = default);

    // Every course mapped to the same subject, across all schools — the bridge
    // from "my CSC201" to "their 6.006".
    IQueryable<Course> GetEquivalentCourses(Guid subjectId, Guid excludeInstitutionId, bool trackChanges);

    IQueryable<Course> GetEnrolledCourses(Guid userId, bool trackChanges);
}
