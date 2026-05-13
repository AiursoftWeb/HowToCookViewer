using Aiursoft.HowToCookViewer.Authorization;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer;

[ExcludeFromCodeCoverage]
public static class ProgramExtends
{
    [ExcludeFromCodeCoverage]
    private static async Task<bool> ShouldSeedAsync(TemplateDbContext dbContext)
    {
        var haveUsers = await dbContext.Users.AnyAsync();
        var haveRoles = await dbContext.Roles.AnyAsync();
        return !haveUsers && !haveRoles;
    }

    [ExcludeFromCodeCoverage]
    public static Task<IHost> CopyAvatarFileAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var storageService = services.GetRequiredService<StorageService>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        var avatarFilePath = Path.Combine(host.Services.GetRequiredService<IHostEnvironment>().ContentRootPath,
            "wwwroot", "images", "default-avatar.jpg");
        var physicalPath = storageService.GetFilePhysicalPath(User.DefaultAvatarPath);
        if (!File.Exists(avatarFilePath))
        {
            logger.LogWarning("Avatar file does not exist. Skip copying.");
            return Task.FromResult(host);
        }

        if (File.Exists(physicalPath))
        {
            logger.LogInformation("Avatar file already exists. Skip copying.");
            return Task.FromResult(host);
        }

        if (!Directory.Exists(Path.GetDirectoryName(physicalPath)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        }

        File.Copy(avatarFilePath, physicalPath);
        logger.LogInformation("Avatar file copied to {Path}", physicalPath);
        return Task.FromResult(host);
    }

    [ExcludeFromCodeCoverage]
    public static async Task<IHost> SeedAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<TemplateDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();

        var settingsService = services.GetRequiredService<GlobalSettingsService>();
        await settingsService.SeedSettingsAsync();

        var shouldSeed = await ShouldSeedAsync(db);
        if (!shouldSeed)
        {
            logger.LogInformation("Do not need to seed the database. There are already users or roles present.");
            return host;
        }

        logger.LogInformation("Seeding the database with initial data...");
        var userManager = services.GetRequiredService<UserManager<User>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        var role = await roleManager.FindByNameAsync("Administrators");
        if (role == null)
        {
            role = new IdentityRole("Administrators");
            await roleManager.CreateAsync(role);
        }

        var existingClaims = await roleManager.GetClaimsAsync(role);
        var existingClaimValues = existingClaims
            .Where(c => c.Type == AppPermissions.Type)
            .Select(c => c.Value)
            .ToHashSet();

        foreach (var permission in AppPermissions.GetAllPermissions())
        {
            if (!existingClaimValues.Contains(permission.Key))
            {
                var claim = new Claim(AppPermissions.Type, permission.Key);
                await roleManager.AddClaimAsync(role, claim);
            }
        }

        if (!await db.Users.AnyAsync(u => u.UserName == "admin"))
        {
            var user = new User
            {
                UserName = "admin",
                DisplayName = "Super Administrator",
                Email = "admin@default.com",
            };
            _ = await userManager.CreateAsync(user, "Admin@123456!");
            await userManager.AddToRoleAsync(user, "Administrators");
        }

        return host;
    }
}
