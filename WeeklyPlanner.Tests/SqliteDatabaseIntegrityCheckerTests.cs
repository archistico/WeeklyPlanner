using Dapper;
using Microsoft.Data.Sqlite;
using WeeklyPlanner.Core.Data;
using Xunit;

namespace WeeklyPlanner.Tests;

public sealed class SqliteDatabaseIntegrityCheckerTests
{
    [Fact]
    public void EnsureIntegrity_accepts_a_valid_database()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.Execute(
            """
            PRAGMA foreign_keys = ON;
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER NOT NULL REFERENCES Parent(Id)
            );
            INSERT INTO Parent (Id) VALUES (1);
            INSERT INTO Child (Id, ParentId) VALUES (1, 1);
            """);

        new SqliteDatabaseIntegrityChecker().EnsureIntegrity(
            connection,
            "durante il test");
    }

    [Fact]
    public void EnsureIntegrity_rejects_foreign_key_violations()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.Execute(
            """
            PRAGMA foreign_keys = OFF;
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER NOT NULL REFERENCES Parent(Id)
            );
            INSERT INTO Child (Id, ParentId) VALUES (1, 999);
            """);

        var exception = Assert.Throws<DatabaseIntegrityException>(
            () => new SqliteDatabaseIntegrityChecker().EnsureIntegrity(
                connection,
                "prima della migrazione"));

        Assert.Equal("prima della migrazione", exception.OperationDescription);
        Assert.Contains("Child", exception.IntegrityDetails, StringComparison.Ordinal);
        Assert.Contains("Parent", exception.IntegrityDetails, StringComparison.Ordinal);
    }
}
