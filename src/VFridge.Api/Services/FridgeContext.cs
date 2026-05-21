using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Data;

namespace VFridge.Api.Services;

/// <summary>
/// Resolves the fridge a request operates on. Reads the X-Fridge-Id header (validates membership)
/// or falls back to the user's first owned fridge. Used by every endpoint that touches products.
/// </summary>
public sealed class FridgeContext(VFridgeDbContext db, ICurrentUser me)
{
    /// <summary>Returns (fridgeId, role) or null if the user is unauthenticated or not a member of the requested fridge.</summary>
    public async Task<(int FridgeId, string Role)?> ResolveAsync(CancellationToken ct)
    {
        if (me.UserId is not int uid) return null;

        if (me.RequestedFridgeId is { } requested)
        {
            var member = await db.FridgeMembers
                .FirstOrDefaultAsync(m => m.FridgeId == requested && m.UserId == uid, ct);
            if (member is null) return null;
            return (requested, member.Role);
        }

        // No explicit fridge requested — use the caller's owned fridge (every user has one
        // after migration 005). Pick the lowest id to keep the choice stable across requests.
        var personal = await db.FridgeMembers
            .Where(m => m.UserId == uid)
            .OrderBy(m => m.FridgeId)
            .FirstOrDefaultAsync(ct);
        if (personal is null) return null;
        return (personal.FridgeId, personal.Role);
    }
}
