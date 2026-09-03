using ExpenseTracker.Api.Models;
using ExpenseTracker.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Data;

/// <summary>
/// The cloud database — the source of truth every device syncs against, and the database the
/// web UI reads directly.
/// </summary>
/// <remarks>
/// Deliberately mirrors the device replica's shape: real foreign keys and navigation
/// properties, so the same repositories and components work against either. The only
/// additions are <c>UserId</c> (the cloud is multi-user) and <c>ClientUpdatedAt</c>
/// (conflict resolution). Translating between a device's local integer keys and the
/// cross-device <c>SyncId</c> happens at the sync boundary in SyncController, not here.
///
/// Note there is no soft-delete query filter, unlike the device context: pull has to return
/// tombstones so deletions can propagate. Callers that want live rows must filter
/// <c>IsDeleted</c> themselves — the repositories in Data/Repositories do.
/// </remarks>
public class ApiDbContext(DbContextOptions<ApiDbContext> options)
    : IdentityDbContext<AppUser>(options), IDataProtectionKeyContext
{
    /// <summary>
    /// Data Protection keys, which sign and encrypt the web sign-in cookie. Kept in the
    /// database rather than on disk so a restart or a second instance does not invalidate
    /// every session.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Income> Incomes => Set<Income>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Bounded so a client cannot push an unbounded string into the account row. Both are
        // short codes ("PLN", "pl") that every client validates against its own list on read.
        modelBuilder.Entity<AppUser>(b =>
        {
            b.Property(u => u.Currency).IsRequired().HasMaxLength(8);
            b.Property(u => u.Language).IsRequired().HasMaxLength(16);
        });

        modelBuilder.Entity<Category>(b =>
        {
            b.Property(c => c.Name).IsRequired().HasMaxLength(64);
            b.Property(c => c.UserId).IsRequired();
            // Deliberately NOT unique on (UserId, Name). SyncId is the identity here, and a
            // device may legitimately push a category whose name matches a built-in one —
            // enforcing name uniqueness made the whole push fail with a 500.
            // The device database keeps its unique name index; it has a single user and a UI
            // that can reject duplicates up front.
            b.HasIndex(c => new { c.UserId, c.Name });
        });

        modelBuilder.Entity<Expense>(b =>
        {
            b.Property(e => e.Description).IsRequired().HasMaxLength(256);
            b.Property(e => e.Amount).HasPrecision(18, 2);
            b.Property(e => e.UserId).IsRequired();
            b.HasOne(e => e.Category)
             .WithMany(c => c.Expenses)
             .HasForeignKey(e => e.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Subscription>(b =>
        {
            b.Property(s => s.Name).IsRequired().HasMaxLength(128);
            b.Property(s => s.Amount).HasPrecision(18, 2);
            b.Property(s => s.BillingCycle).HasConversion<string>();
            b.Property(s => s.UserId).IsRequired();
            b.HasOne(s => s.Category)
             .WithMany(c => c.Subscriptions)
             .HasForeignKey(s => s.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Income>(b =>
        {
            b.Property(i => i.Description).IsRequired().HasMaxLength(256);
            b.Property(i => i.Amount).HasPrecision(18, 2);
            b.Property(i => i.BillingCycle).HasConversion<string>();
            b.Property(i => i.UserId).IsRequired();
        });

        // Push resolves each incoming row by (UserId, SyncId); without this the upsert is a
        // table scan per row.
        modelBuilder.Entity<Expense>().HasIndex(e => new { e.UserId, e.SyncId }).IsUnique();
        modelBuilder.Entity<Income>().HasIndex(i => new { i.UserId, i.SyncId }).IsUnique();
        modelBuilder.Entity<Subscription>().HasIndex(s => new { s.UserId, s.SyncId }).IsUnique();
        modelBuilder.Entity<Category>().HasIndex(c => new { c.UserId, c.SyncId }).IsUnique();
    }
}
