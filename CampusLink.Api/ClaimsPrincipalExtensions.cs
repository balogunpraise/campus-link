using System.Security.Claims;

namespace CampusLink.Api;

public static class ClaimsPrincipalExtensions
{
    // Safe on any [Authorize] endpoint: the token always carries this claim, so
    // a missing or malformed one means the pipeline let something through that
    // it shouldn't have.
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("Authenticated principal has no usable user id claim.");
    }
}
