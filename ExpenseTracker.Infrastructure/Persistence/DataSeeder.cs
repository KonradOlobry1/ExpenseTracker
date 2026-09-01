using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Persistence;

public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Only seed if no user data exists yet
        if (await db.Expenses.AnyAsync() || await db.Subscriptions.AnyAsync())
            return;

        var today = DateTime.Today;

        // ── Sample expenses spread across last 3 months ──────────────────────
        var expenses = new List<Expense>
        {
            // This month
            new() { Description = "Groceries",         Amount = 87.40m,  CategoryId = 1, Date = today.AddDays(-2)  },
            new() { Description = "Coffee & pastry",   Amount = 12.50m,  CategoryId = 1, Date = today.AddDays(-3)  },
            new() { Description = "Gas",               Amount = 55.00m,  CategoryId = 2, Date = today.AddDays(-5)  },
            new() { Description = "Electricity bill",  Amount = 94.20m,  CategoryId = 6, Date = today.AddDays(-6)  },
            new() { Description = "Restaurant dinner", Amount = 68.00m,  CategoryId = 1, Date = today.AddDays(-8)  },
            new() { Description = "Pharmacy",          Amount = 24.99m,  CategoryId = 4, Date = today.AddDays(-10) },
            new() { Description = "Bus pass",          Amount = 40.00m,  CategoryId = 2, Date = today.AddDays(-12) },
            new() { Description = "Cinema tickets",    Amount = 28.00m,  CategoryId = 5, Date = today.AddDays(-14) },

            // Last month
            new() { Description = "Groceries",         Amount = 102.30m, CategoryId = 1, Date = today.AddMonths(-1).AddDays(-1)  },
            new() { Description = "Rent",              Amount = 1200.00m,CategoryId = 3, Date = today.AddMonths(-1).AddDays(-3)  },
            new() { Description = "Internet bill",     Amount = 49.99m,  CategoryId = 6, Date = today.AddMonths(-1).AddDays(-5)  },
            new() { Description = "Gas",               Amount = 60.00m,  CategoryId = 2, Date = today.AddMonths(-1).AddDays(-7)  },
            new() { Description = "Doctor visit",      Amount = 80.00m,  CategoryId = 4, Date = today.AddMonths(-1).AddDays(-10) },
            new() { Description = "Takeaway",          Amount = 34.50m,  CategoryId = 1, Date = today.AddMonths(-1).AddDays(-12) },
            new() { Description = "Books",             Amount = 45.00m,  CategoryId = 5, Date = today.AddMonths(-1).AddDays(-15) },
            new() { Description = "Gym membership",    Amount = 35.00m,  CategoryId = 4, Date = today.AddMonths(-1).AddDays(-20) },

            // Two months ago
            new() { Description = "Groceries",         Amount = 91.80m,  CategoryId = 1, Date = today.AddMonths(-2).AddDays(-2)  },
            new() { Description = "Rent",              Amount = 1200.00m,CategoryId = 3, Date = today.AddMonths(-2).AddDays(-3)  },
            new() { Description = "Electricity bill",  Amount = 88.60m,  CategoryId = 6, Date = today.AddMonths(-2).AddDays(-6)  },
            new() { Description = "Train tickets",     Amount = 72.00m,  CategoryId = 2, Date = today.AddMonths(-2).AddDays(-9)  },
            new() { Description = "Restaurant lunch",  Amount = 22.00m,  CategoryId = 1, Date = today.AddMonths(-2).AddDays(-11) },
            new() { Description = "Concert ticket",    Amount = 55.00m,  CategoryId = 5, Date = today.AddMonths(-2).AddDays(-14) },
            new() { Description = "Vitamins",          Amount = 19.90m,  CategoryId = 4, Date = today.AddMonths(-2).AddDays(-17) },
            new() { Description = "Parking fees",      Amount = 15.00m,  CategoryId = 2, Date = today.AddMonths(-2).AddDays(-22) },

            // Three months ago
            new() { Description = "Groceries",         Amount = 78.50m,  CategoryId = 1, Date = today.AddMonths(-3).AddDays(-1)  },
            new() { Description = "Rent",              Amount = 1200.00m,CategoryId = 3, Date = today.AddMonths(-3).AddDays(-3)  },
            new() { Description = "Internet bill",     Amount = 49.99m,  CategoryId = 6, Date = today.AddMonths(-3).AddDays(-5)  },
            new() { Description = "Uber",              Amount = 18.40m,  CategoryId = 2, Date = today.AddMonths(-3).AddDays(-8)  },
            new() { Description = "Takeaway",          Amount = 27.80m,  CategoryId = 1, Date = today.AddMonths(-3).AddDays(-13) },
            new() { Description = "Dentist",           Amount = 120.00m, CategoryId = 4, Date = today.AddMonths(-3).AddDays(-16) },
            new() { Description = "Video game",        Amount = 59.99m,  CategoryId = 5, Date = today.AddMonths(-3).AddDays(-20) },
        };

        foreach (var e in expenses)
            e.CreatedAt = e.Date.ToUniversalTime();

        db.Expenses.AddRange(expenses);

        // ── Sample subscriptions ─────────────────────────────────────────────
        var subscriptions = new List<Subscription>
        {
            new() { Name = "Netflix",      Amount = 15.99m,  BillingCycle = BillingCycle.Monthly,  CategoryId = 5, StartDate = today.AddMonths(-8),  IsActive = true,  Url = "https://netflix.com"   },
            new() { Name = "Spotify",      Amount = 9.99m,   BillingCycle = BillingCycle.Monthly,  CategoryId = 5, StartDate = today.AddMonths(-14), IsActive = true,  Url = "https://spotify.com"   },
            new() { Name = "iCloud 50GB",  Amount = 0.99m,   BillingCycle = BillingCycle.Monthly,  CategoryId = 7, StartDate = today.AddMonths(-6),  IsActive = true                                 },
            new() { Name = "GitHub Pro",   Amount = 4.00m,   BillingCycle = BillingCycle.Monthly,  CategoryId = 7, StartDate = today.AddMonths(-10), IsActive = true,  Url = "https://github.com"    },
            new() { Name = "Gym",          Amount = 35.00m,  BillingCycle = BillingCycle.Monthly,  CategoryId = 4, StartDate = today.AddMonths(-4),  IsActive = true                                 },
            new() { Name = "Adobe CC",     Amount = 59.99m,  BillingCycle = BillingCycle.Monthly,  CategoryId = 7, StartDate = today.AddMonths(-24), IsActive = true,  Url = "https://adobe.com"     },
            new() { Name = "Domain name",  Amount = 15.00m,  BillingCycle = BillingCycle.Yearly,   CategoryId = 7, StartDate = today.AddMonths(-18), IsActive = true                                 },
            new() { Name = "Amazon Prime", Amount = 139.00m, BillingCycle = BillingCycle.Yearly,   CategoryId = 5, StartDate = today.AddMonths(-5),  IsActive = true,  Url = "https://amazon.com"    },
            new() { Name = "Antivirus",    Amount = 29.99m,  BillingCycle = BillingCycle.Yearly,   CategoryId = 7, StartDate = today.AddMonths(-11), IsActive = true                                 },
            new() { Name = "HBO Max",      Amount = 9.99m,   BillingCycle = BillingCycle.Monthly,  CategoryId = 5, StartDate = today.AddMonths(-7),  IsActive = false, Notes = "Cancelled"           },
        };

        foreach (var s in subscriptions)
            s.CreatedAt = s.StartDate.ToUniversalTime();

        db.Subscriptions.AddRange(subscriptions);

        await db.SaveChangesAsync();
    }
}
