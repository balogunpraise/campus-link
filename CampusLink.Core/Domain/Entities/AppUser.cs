using Microsoft.AspNetCore.Identity;

namespace CampusLink.Core.Domain.Entities;

public class AppUser : IdentityUser<Guid>
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
}