using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Migrations
{
    /// <summary>
    /// AddSyncMetadata introduced SyncId with a constant all-zero default, because SQLite
    /// rejects non-constant defaults in ALTER TABLE ADD COLUMN. New rows get a real UUID
    /// from HasDefaultValueSql, but every row that already existed — including the seven
    /// seeded categories — kept the zero GUID.
    ///
    /// That made SyncId useless as a cross-device identity: all categories collided on one
    /// value, and every expense pointed at that same value, so a sync would map the entire
    /// history onto a single category.
    ///
    /// UPDATE has no constant-default restriction, so the fix is a straight backfill.
    /// </summary>
    public partial class BackfillSyncIds : Migration
    {
        // Same generator AppDbContext uses for new rows.
        private const string NewUuid =
            "(lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || " +
            "substr(lower(hex(randomblob(2))),2) || '-' || substr('89ab',abs(random()) % 4 + 1, 1) || " +
            "substr(lower(hex(randomblob(2))),2) || '-' || lower(hex(randomblob(6))))";

        private const string Zero = "'00000000-0000-0000-0000-000000000000'";

        // Changing a row's SyncId changes its cross-device identity, so the row counts as
        // modified and has to be re-pushed. Sync selects by UpdatedAt, so stamp it in the
        // same statement — a later UPDATE could not find these rows again.
        private const string Now = "strftime('%Y-%m-%d %H:%M:%f', 'now')";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expenses and subscriptions are pushed with their category's SyncId resolved at
            // push time, so every row hanging off a zero-GUID category has already been sent
            // upstream pointing at the wrong identity. Mark them dirty first — once the
            // categories below are fixed, this WHERE clause can no longer find them.
            foreach (var table in new[] { "Expenses", "Subscriptions" })
            {
                migrationBuilder.Sql(
                    $"UPDATE {table} SET UpdatedAt = {Now} WHERE CategoryId IN " +
                    $"(SELECT Id FROM Categories WHERE SyncId = {Zero});");
            }

            // Seeded categories get the same fixed SyncIds that HasData declares, so a
            // device upgraded through this migration and a freshly installed one agree.
            for (var id = 1; id <= 7; id++)
            {
                migrationBuilder.Sql(
                    $"UPDATE Categories SET SyncId = '11111111-0000-0000-0000-00000000000{id}', " +
                    $"UpdatedAt = {Now} WHERE Id = {id} AND SyncId = {Zero};");
            }

            // Anything else still holding the zero GUID — user-created categories and all
            // pre-existing expenses, incomes and subscriptions — gets a fresh UUID.
            foreach (var table in new[] { "Categories", "Expenses", "Incomes", "Subscriptions" })
            {
                migrationBuilder.Sql(
                    $"UPDATE {table} SET SyncId = {NewUuid}, UpdatedAt = {Now} WHERE SyncId = {Zero};");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty: restoring the zero GUIDs would re-introduce the collision
            // and there is no way to tell backfilled rows from legitimately new ones.
        }
    }
}
