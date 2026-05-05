using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aiursoft.HowToCookViewer.MySql;

// This class will be scanned by Entity framework during migrations adding. Do NOT delete!
// On production, real database will respect the appsettings.json.
[ExcludeFromCodeCoverage]
public class MySqlContextFactory : IDesignTimeDbContextFactory<MySqlContext>
{
    public MySqlContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MySqlContext>();

        // ⚠️ DESIGN-TIME ONLY: This factory is ONLY used by 'dotnet ef migrations' commands.
        // It is NEVER used at runtime. The actual database connection comes from appsettings.json.
        // This placeholder connection string is just to satisfy EF Core's type system during schema generation.
        optionsBuilder.UseMySql(
            "Server=design-time-placeholder;Database=design-time-placeholder;Uid=placeholder;Pwd=placeholder;",
            new MySqlServerVersion(new Version(9, 5, 0)));

        return new MySqlContext(optionsBuilder.Options);
    }
}
