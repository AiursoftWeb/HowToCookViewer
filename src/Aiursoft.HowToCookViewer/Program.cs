using System.Diagnostics.CodeAnalysis;
using Aiursoft.ClickhouseLoggerProvider;
using Aiursoft.DbTools;
using Aiursoft.HowToCookViewer.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.HowToCookViewer;

[ExcludeFromCodeCoverage]
public abstract class Program
{
    public static async Task Main(string[] args)
    {
        var app = await AppAsync<Startup>(args);
        await app.Services.InitLoggingTableAsync();
        await app.UpdateDbAsync<TemplateDbContext>();
        await app.SeedAsync();
        await app.CopyAvatarFileAsync();
        await app.RunAsync();
    }
}
