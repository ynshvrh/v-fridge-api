namespace VFridge.Api.Auth;

public interface ICurrentUser
{
    /// <summary>Returns the authenticated user id or null if no user is bound to the request.</summary>
    int? UserId { get; }

    /// <summary>
    /// Returns the explicit X-Fridge-Id header value if the request supplied one, otherwise null.
    /// Endpoints fall back to the caller's first owned fridge when this is null.
    /// </summary>
    int? RequestedFridgeId { get; }
}
