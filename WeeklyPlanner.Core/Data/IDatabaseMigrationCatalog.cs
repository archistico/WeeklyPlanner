namespace WeeklyPlanner.Core.Data;

public interface IDatabaseMigrationCatalog
{
    IReadOnlyList<DatabaseMigration> ReadMigrations();
}
