using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Data;

/// <summary>
/// Creates the database on first run, and adds any columns a newer build expects but an
/// existing database does not have. Without this, upgrading the app would mean deleting
/// the history it exists to accumulate.
/// </summary>
public static class DatabaseInitializer
{
    private static readonly (string Table, string Column, string Type)[] ExpectedColumns =
    {
        ("DiagnosticFiles", "Notes", "TEXT NULL"),
        ("DiagnosticFiles", "TicketNumber", "TEXT NULL"),
        ("DiagnosticFiles", "IsBaseline", "INTEGER NOT NULL DEFAULT 0"),
        ("DiagnosticFiles", "ZohoTicketId", "TEXT NULL"),
        ("DiagnosticFiles", "ZohoTicketNumber", "TEXT NULL"),
        ("DiagnosticFiles", "ZohoTicketCreatedUtc", "TEXT NULL"),
        ("DiagnosticFiles", "AlertSentUtc", "TEXT NULL")
    };

    public static void Initialize(DiagDbContext context)
    {
        context.Database.EnsureCreated();

        foreach (var (table, column, type) in ExpectedColumns)
        {
            if (!ColumnExists(context, table, column))
            {
                // EF1002: SQLite cannot parameterise table/column names, and these come from the
                // constant list above rather than from anything a user can influence.
#pragma warning disable EF1002
                context.Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {type};");
#pragma warning restore EF1002
            }
        }
    }

    private static bool ColumnExists(DiagDbContext context, string table, string column)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table}\");";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
    }
}
