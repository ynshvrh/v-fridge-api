using System.Security.Claims;

namespace VFridge.Api.Auth;

/// <summary>
/// Reads the current user id from the request principal (JWT "sub" claim) and falls back
/// to the X-User-Id header in Development. The header path is removed once JWT auth lands.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor, IHostEnvironment env) : ICurrentUser
{
    public int? UserId
    {
        get
        {
            var ctx = accessor.HttpContext;
            if (ctx is null) return null;

            var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? ctx.User.FindFirstValue("sub");
            if (int.TryParse(sub, out var id)) return id;

            if (env.IsDevelopment()
                && ctx.Request.Headers.TryGetValue("X-User-Id", out var headerVal)
                && int.TryParse(headerVal, out var headerId))
            {
                return headerId;
            }

            return null;
        }
    }

    public int? RequestedFridgeId
    {
        get
        {
            var ctx = accessor.HttpContext;
            if (ctx is null) return null;
            if (ctx.Request.Headers.TryGetValue("X-Fridge-Id", out var raw)
                && int.TryParse(raw, out var id))
            {
                return id;
            }
            return null;
        }
    }
}
