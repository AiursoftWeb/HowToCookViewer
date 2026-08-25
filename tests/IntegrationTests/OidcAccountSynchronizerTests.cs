// ReSharper disable RedundantUsingDirective
// ReSharper disable RedundantSuppressNullableWarningExpression

using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
[DoNotParallelize]
public class OidcAccountSynchronizerTests : TestBase
{
    [TestMethod]
    public async Task UserNameCollisionDoesNotBindTheExistingAccount()
    {
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var synchronizer = scope.ServiceProvider.GetRequiredService<OidcAccountSynchronizer>();
        var suffix = Guid.NewGuid().ToString("N");
        var victim = CreateUser($"victim-{suffix}", "Victim", $"victim-{suffix}@example.com");
        Assert.IsTrue((await userManager.CreateAsync(victim)).Succeeded);

        var result = await synchronizer.SynchronizeAsync(new OidcUserProfile(
            LoginProvider: "OpenIdConnect",
            ProviderKey: $"attacker-sub-{suffix}",
            UserName: $"victim-{suffix}",
            DisplayName: "Attacker",
            Email: $"attacker-{suffix}@example.com",
            Roles: new HashSet<string>()));

        Assert.IsTrue(result.Succeeded);
        var attacker = await userManager.FindByLoginAsync("OpenIdConnect", $"attacker-sub-{suffix}");
        Assert.IsNotNull(attacker);
        Assert.AreNotEqual(victim.Id, attacker.Id);
        var unchangedVictim = await userManager.FindByIdAsync(victim.Id);
        Assert.AreEqual($"victim-{suffix}@example.com", unchangedVictim?.Email);
    }

    [TestMethod]
    public async Task FirstCompatibilityBindingUsesUniqueEmail()
    {
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var synchronizer = scope.ServiceProvider.GetRequiredService<OidcAccountSynchronizer>();
        var suffix = Guid.NewGuid().ToString("N");
        var existingUser = CreateUser(
            $"local-{suffix}",
            "Local User",
            $"person-{suffix}@example.com");
        Assert.IsTrue((await userManager.CreateAsync(existingUser)).Succeeded);

        var result = await synchronizer.SynchronizeAsync(new OidcUserProfile(
            LoginProvider: "OpenIdConnect",
            ProviderKey: $"subject-{suffix}",
            UserName: $"external-{suffix}",
            DisplayName: "External User",
            Email: existingUser.Email!,
            Roles: new HashSet<string>()));

        Assert.IsTrue(result.Succeeded);
        var boundUser = await userManager.FindByLoginAsync("OpenIdConnect", $"subject-{suffix}");
        Assert.AreEqual(existingUser.Id, boundUser?.Id);
        Assert.AreEqual(existingUser.UserName, boundUser?.UserName);
        Assert.AreEqual("External User", boundUser?.DisplayName);
    }

    [TestMethod]
    public async Task ExistingSubjectBindingWinsOverAUserNameCollision()
    {
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var synchronizer = scope.ServiceProvider.GetRequiredService<OidcAccountSynchronizer>();
        var suffix = Guid.NewGuid().ToString("N");
        var linkedUser = CreateUser($"linked-{suffix}", "Linked", $"linked-{suffix}@example.com");
        var collisionUser = CreateUser(
            $"collision-{suffix}",
            "Collision",
            $"collision-{suffix}@example.com");
        Assert.IsTrue((await userManager.CreateAsync(linkedUser)).Succeeded);
        Assert.IsTrue((await userManager.CreateAsync(collisionUser)).Succeeded);
        Assert.IsTrue((await userManager.AddLoginAsync(
            linkedUser,
            new UserLoginInfo("OpenIdConnect", $"stable-sub-{suffix}", "OpenIdConnect"))).Succeeded);

        var result = await synchronizer.SynchronizeAsync(new OidcUserProfile(
            LoginProvider: "OpenIdConnect",
            ProviderKey: $"stable-sub-{suffix}",
            UserName: collisionUser.UserName!,
            DisplayName: "Still Linked",
            Email: linkedUser.Email!,
            Roles: new HashSet<string>()));

        Assert.IsTrue(result.Succeeded);
        var boundUser = await userManager.FindByLoginAsync("OpenIdConnect", $"stable-sub-{suffix}");
        Assert.AreEqual(linkedUser.Id, boundUser?.Id);
        Assert.AreEqual(collisionUser.UserName, (await userManager.FindByIdAsync(collisionUser.Id))?.UserName);
    }

    [TestMethod]
    public async Task InvalidRoleFailsTheWholeSynchronizationResult()
    {
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var synchronizer = scope.ServiceProvider.GetRequiredService<OidcAccountSynchronizer>();
        var suffix = Guid.NewGuid().ToString("N");
        var providerKey = $"invalid-role-sub-{suffix}";

        var result = await synchronizer.SynchronizeAsync(new OidcUserProfile(
            LoginProvider: "OpenIdConnect",
            ProviderKey: providerKey,
            UserName: $"invalid-role-{suffix}",
            DisplayName: "Invalid Role",
            Email: $"invalid-role-{suffix}@example.com",
            Roles: new HashSet<string> { new('r', 300) }));

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(await userManager.FindByLoginAsync("OpenIdConnect", providerKey));
    }

    private static User CreateUser(string userName, string displayName, string email)
    {
        var user = Activator.CreateInstance<User>();
        user.UserName = userName;
        user.DisplayName = displayName;
        user.Email = email;
        return user;
    }
}
