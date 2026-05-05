using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aiursoft.HowToCookViewer.Sqlite;

// This class will be scanned by Entity framework during migrations adding. Do NOT delete!
// On production, real database will respect the appsettings.json.
[ExcludeFromCodeCoverage]
public class SqliteContextFactory : IDesignTimeDbContextFactory<SqliteContext>
{
    public SqliteContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SqliteContext>();

        // ⚠️ DESIGN-TIME ONLY: This factory is ONLY used by 'dotnet ef migrations' commands.
        // It is NEVER used at runtime. The actual database connection comes from appsettings.json.
        // This placeholder connection string is just to satisfy EF Core's type system during schema generation.
        optionsBuilder.UseSqlite("DataSource=design-time-placeholder.db");

        return new SqliteContext(optionsBuilder.Options);
    }
}
