using CampusLink.Core.Domain.Enums;

namespace CampusLink.Core.Domain.Entities;

// An agreement between two schools (sister campuses, a city-wide consortium,
// an exchange programme) that widens sharing without going fully global.
// Stored once per pair; treat it as undirected — store the smaller Id first so
// the unique index can't hold both (A,B) and (B,A).
public class InstitutionPartnership : BaseEntity
{
    public Guid InstitutionAId { get; set; }
    public Institution InstitutionA { get; set; } = null!;

    public Guid InstitutionBId { get; set; }
    public Institution InstitutionB { get; set; } = null!;

    public PartnershipStatus Status { get; set; } = PartnershipStatus.Pending;
    public string Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
