using CampusLink.Core.Application.Interfaces.Repositories;
using CampusLink.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data.Repositories;

public class SubjectRepository(AppDbContext context)
    : RepositoryBase<Subject>(context), ISubjectRepository
{
    public Task<Subject> GetBySlugAsync(
        string slug,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(s => s.Slug == slug.ToLower(), trackChanges)
            .FirstOrDefaultAsync(cancellationToken);

    public IQueryable<Subject> SearchApproved(string term, string field, bool trackChanges)
    {
        var query = FindByCondition(s => s.IsApproved, trackChanges);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";
            query = query.Where(s => EF.Functions.Like(s.Name, pattern));
        }

        if (!string.IsNullOrWhiteSpace(field))
            query = query.Where(s => s.Field == field);

        return query.OrderBy(s => s.Name);
    }

    public IQueryable<Subject> GetPendingApproval(bool trackChanges) =>
        FindByCondition(s => !s.IsApproved, trackChanges).OrderBy(s => s.Name);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) =>
        ExistsAsync(s => s.Slug == slug.ToLower(), cancellationToken);
}

public class CourseRepository(AppDbContext context)
    : RepositoryBase<Course>(context), ICourseRepository
{
    public IQueryable<Course> GetForInstitution(Guid institutionId, bool trackChanges) =>
        FindByCondition(c => c.InstitutionId == institutionId, trackChanges)
            .OrderBy(c => c.Code);

    public Task<Course> GetByCodeAsync(
        Guid institutionId,
        string code,
        bool trackChanges,
        CancellationToken cancellationToken = default) =>
        FindByCondition(c => c.InstitutionId == institutionId && c.Code == code, trackChanges)
            .FirstOrDefaultAsync(cancellationToken);

    public IQueryable<Course> GetEquivalentCourses(Guid subjectId, Guid excludeInstitutionId, bool trackChanges) =>
        FindByCondition(c => c.SubjectId == subjectId && c.InstitutionId != excludeInstitutionId, trackChanges)
            .Include(c => c.Institution)
            .OrderBy(c => c.Institution.Name)
            .ThenBy(c => c.Code);

    public IQueryable<Course> GetEnrolledCourses(Guid userId, bool trackChanges) =>
        FindAll(trackChanges)
            .Where(c => c.Enrollments.Any(e => e.UserId == userId))
            .OrderBy(c => c.Code);
}
