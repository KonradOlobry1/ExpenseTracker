using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Income> Incomes => Set<Income>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The device replica holds one user's data and never resolves conflicts itself, so
        // the cloud-only columns are not mapped here.
        foreach (var entity in new[] { typeof(Expense), typeof(Category), typeof(Income), typeof(Subscription) })
        {
            modelBuilder.Entity(entity).Ignore(nameof(ISyncEntity.UserId));
            modelBuilder.Entity(entity).Ignore(nameof(ISyncEntity.ClientUpdatedAt));
        }

        const string syncIdDefault = "(lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))),2) || '-' || substr('89ab',abs(random()) % 4 + 1, 1) || substr(lower(hex(randomblob(2))),2) || '-' || lower(hex(randomblob(6))))";

        modelBuilder.Entity<Category>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).IsRequired().HasMaxLength(64);
            // Filtered so a soft-deleted category does not permanently reserve its name.
            b.HasIndex(c => c.Name).IsUnique().HasFilter("IsDeleted = 0");
            b.Property(c => c.SyncId).HasDefaultValueSql(syncIdDefault);
            b.HasQueryFilter(c => !c.IsDeleted);
        });

        modelBuilder.Entity<Expense>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Description).IsRequired().HasMaxLength(256);
            b.Property(e => e.Amount).HasColumnType("TEXT");
            b.Property(e => e.SyncId).HasDefaultValueSql(syncIdDefault);
            b.HasOne(e => e.Category)
             .WithMany(c => c.Expenses)
             .HasForeignKey(e => e.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(e => !e.IsDeleted && !e.Category.IsDeleted);
        });

        modelBuilder.Entity<Subscription>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).IsRequired().HasMaxLength(128);
            b.Property(s => s.Amount).HasColumnType("TEXT");
            b.Property(s => s.BillingCycle).HasConversion<string>();
            b.Property(s => s.SyncId).HasDefaultValueSql(syncIdDefault);
            b.HasOne(s => s.Category)
             .WithMany(c => c.Subscriptions)
             .HasForeignKey(s => s.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
            b.HasQueryFilter(s => !s.IsDeleted && !s.Category.IsDeleted);
        });

        modelBuilder.Entity<Income>(b =>
        {
            b.HasKey(i => i.Id);
            b.Property(i => i.Description).IsRequired().HasMaxLength(256);
            b.Property(i => i.Amount).HasColumnType("TEXT");
            b.Property(i => i.BillingCycle).HasConversion<string>();
            b.Property(i => i.SyncId).HasDefaultValueSql(syncIdDefault);
            b.HasQueryFilter(i => !i.IsDeleted);
        });

        // SyncId and CreatedAt MUST be hardcoded static values here.
        // Property initializers (Guid.NewGuid(), DateTime.UtcNow) run on every model build
        // and make EF think there are always pending changes (PendingModelChangesWarning).
        var seedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        modelBuilder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Food",          Icon = "restaurant",     Color = "#F44336", IsSystem = true, SyncId = new Guid("11111111-0000-0000-0000-000000000001"), CreatedAt = seedDate },
            new Category { Id = 2, Name = "Transport",     Icon = "directions_car", Color = "#2196F3", IsSystem = true, SyncId = new Guid("11111111-0000-0000-0000-000000000002"), CreatedAt = seedDate },
            new Category { Id = 3, Name = "Housing",       Icon = "home",           Color = "#4CAF50", IsSystem = true, SyncId = new Guid("11111111-0000-0000-0000-000000000003"), CreatedAt = seedDate },
            new Category { Id = 4, Name = "Health",        Icon = "favorite",       Color = "#E91E63", IsSystem = true, SyncId = new Guid("11111111-0000-0000-0000-000000000004"), CreatedAt = seedDate },
            new Category { Id = 5, Name = "Entertainment", Icon = "movie",          Color = "#9C27B0", IsSystem = true, SyncId = new Guid("11111111-0000-0000-0000-000000000005"), CreatedAt = seedDate },
            new Category { Id = 6, Name = "Utilities",     Icon = "bolt",           Color = "#FF9800", IsSystem = true, SyncId = new Guid("11111111-0000-0000-0000-000000000006"), CreatedAt = seedDate },
            new Category { Id = 7, Name = "Other",         Icon = "more_horiz",     Color = "#607D8B", IsSystem = true, SyncId = new Guid("11111111-0000-0000-0000-000000000007"), CreatedAt = seedDate }
        );
    }
}
