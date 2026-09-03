using System.Security.Claims;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/sync")]
[Authorize]
[EnableRateLimiting("sync")]
public class SyncController(ApiDbContext db) : ControllerBase
{
    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();

    [HttpPost("push")]
    public async Task<IActionResult> Push([FromBody] SyncPushRequest request)
    {
        var userId = CurrentUserId;
        var now = DateTime.UtcNow;

        // SyncId is the cross-device identity for every row. An all-zero GUID means the
        // client never assigned one, and accepting it would collapse unrelated rows onto a
        // single identity — categories especially, which expenses join against.
        if (HasEmptySyncId(request))
            return BadRequest(new
            {
                error = "One or more items have an empty SyncId. Update the app so its "
                      + "local database is migrated before syncing."
            });

        // Categories first, and saved, so the rows that reference them can resolve their
        // freshly assigned keys.
        await UpsertAsync(db.Categories, request.Categories, userId, now,
            d => d.SyncId, (d, e) => d.Apply(e), d => d.ToEntity(userId));
        await db.SaveChangesAsync();

        var categoryIdBySyncId = await db.Categories
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.SyncId, c => c.Id);

        // A row whose category this account has never seen is skipped rather than dropped
        // on the floor with a null FK; the next sync brings it once the category arrives.
        int? Resolve(Guid syncId) =>
            categoryIdBySyncId.TryGetValue(syncId, out var id) ? id : null;

        await UpsertAsync(db.Expenses, request.Expenses, userId, now,
            d => d.SyncId,
            (d, e) => d.Apply(e, Resolve(d.CategorySyncId)!.Value),
            d => Resolve(d.CategorySyncId) is { } id ? d.ToEntity(userId, id) : null,
            d => Resolve(d.CategorySyncId) is not null);

        await UpsertAsync(db.Subscriptions, request.Subscriptions, userId, now,
            d => d.SyncId,
            (d, e) => d.Apply(e, Resolve(d.CategorySyncId)!.Value),
            d => Resolve(d.CategorySyncId) is { } id ? d.ToEntity(userId, id) : null,
            d => Resolve(d.CategorySyncId) is not null);

        await UpsertAsync(db.Incomes, request.Incomes, userId, now,
            d => d.SyncId, (d, e) => d.Apply(e), d => d.ToEntity(userId));

        await ApplySettingsAsync(request.Settings, userId);

        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// Settings are one record per account, so there is nothing to match on — only the stamp
    /// decides. A client that has never changed them sends <see cref="DateTime.MinValue"/>,
    /// which loses to anything already stored and so cannot wipe another device's choice.
    /// The values themselves are not validated here: every client resolves an unknown
    /// currency or language back to its own default when reading.
    /// </summary>
    private async Task ApplySettingsAsync(SyncSettingsDto? settings, string userId)
    {
        if (settings is null) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || settings.UpdatedAt <= user.SettingsUpdatedAt) return;

        user.Currency = settings.Currency;
        user.Language = settings.Language;
        user.IsDarkMode = settings.IsDarkMode;
        user.SettingsUpdatedAt = settings.UpdatedAt;
    }

    /// <summary>
    /// Upserts one batch by (UserId, SyncId), skipping rows the server already holds a newer
    /// copy of. Resolution is by the client's own edit stamp: push order used to decide the
    /// winner, so whichever device synced last won even when its edit was older.
    /// </summary>
    /// <remarks>
    /// The whole batch is resolved with one query. This used to run a FirstOrDefaultAsync per
    /// row, so a phone pushing forty rows made forty round trips to a database on the other
    /// side of the internet — on a link where latency, not the query, is the cost.
    ///
    /// (UserId, SyncId) is unique, so keying the lookup by SyncId within one account is safe.
    /// </remarks>
    private async Task UpsertAsync<TDto, TEntity>(
        DbSet<TEntity> set, List<TDto>? items, string userId, DateTime now,
        Func<TDto, Guid> syncId, Action<TDto, TEntity> apply, Func<TDto, TEntity?> create,
        Func<TDto, bool>? canApply = null)
        where TEntity : class, ISyncEntity
    {
        if (items is not { Count: > 0 }) return;

        var incomingIds = items.Select(syncId).ToHashSet();
        var bySyncId = await set
            .Where(e => e.UserId == userId && incomingIds.Contains(e.SyncId))
            .ToDictionaryAsync(e => e.SyncId);

        foreach (var dto in items)
        {
            if (canApply is not null && !canApply(dto)) continue;

            // Materialised once and reused for both the insert and the timestamp comparison.
            // It used to be built twice per row, the second copy thrown away after reading a
            // single property off it.
            if (create(dto) is not { } incoming) continue;

            var id = syncId(dto);

            if (!bySyncId.TryGetValue(id, out var existing))
            {
                incoming.UpdatedAt = now;
                set.Add(incoming);

                // A batch that carries the same SyncId twice now merges into one row. The
                // per-row lookup could not see rows added earlier in the same batch, so it
                // inserted both and broke the unique index at SaveChanges.
                bySyncId[id] = incoming;
                continue;
            }

            if (existing.ClientUpdatedAt != default && incoming.ClientUpdatedAt < existing.ClientUpdatedAt)
                continue;

            apply(dto, existing);
            existing.UpdatedAt = now;
        }
    }

    [HttpGet("pull")]
    public async Task<ActionResult<SyncPullResponse>> Pull([FromQuery] DateTime? since)
    {
        var userId = CurrentUserId;
        var sinceTime = since ?? DateTime.MinValue;

        var categorySyncIdById = await db.Categories
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.Id, c => c.SyncId);

        Guid SyncIdOf(int categoryId) =>
            categorySyncIdById.TryGetValue(categoryId, out var s) ? s : Guid.Empty;

        // Settings ignore `since`. They are four fields, and a client that filtered them out
        // as unchanged would have no way to notice a value it had never seen.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);

        return Ok(new SyncPullResponse(
            (await Changed(db.Expenses, userId, sinceTime)).Select(e => e.ToDto(SyncIdOf(e.CategoryId))).ToList(),
            (await Changed(db.Incomes, userId, sinceTime)).Select(i => i.ToDto()).ToList(),
            (await Changed(db.Subscriptions, userId, sinceTime)).Select(s => s.ToDto(SyncIdOf(s.CategoryId))).ToList(),
            (await Changed(db.Categories, userId, sinceTime)).Select(c => c.ToDto()).ToList(),
            DateTime.UtcNow,
            user is null
                ? null
                : new SyncSettingsDto(user.Currency, user.Language, user.IsDarkMode, user.SettingsUpdatedAt)));
    }

    /// <summary>Rows for this user changed since the given server time; all of them if unset.</summary>
    private static Task<List<TEntity>> Changed<TEntity>(
        DbSet<TEntity> set, string userId, DateTime since)
        where TEntity : class, ISyncEntity
    {
        var query = set.Where(e => e.UserId == userId);

        if (since != DateTime.MinValue)
            query = query.Where(e => (e.UpdatedAt != null && e.UpdatedAt > since)
                                  || (e.UpdatedAt == null && e.CreatedAt > since));

        return query.ToListAsync();
    }

    private static bool HasEmptySyncId(SyncPushRequest request) =>
        (request.Expenses?.Any(x => x.SyncId == Guid.Empty || x.CategorySyncId == Guid.Empty) ?? false)
        || (request.Incomes?.Any(x => x.SyncId == Guid.Empty) ?? false)
        || (request.Subscriptions?.Any(x => x.SyncId == Guid.Empty || x.CategorySyncId == Guid.Empty) ?? false)
        || (request.Categories?.Any(x => x.SyncId == Guid.Empty) ?? false);
}
