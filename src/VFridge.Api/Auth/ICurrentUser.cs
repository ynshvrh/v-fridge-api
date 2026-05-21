namespace VFridge.Api.Auth;

public interface ICurrentUser
{
    /// <summary>Returns the authenticated user id or null if no user is bound to the request.</summary>
    int? UserId { get; }
}
