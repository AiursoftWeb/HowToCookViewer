using System.Diagnostics.CodeAnalysis;
using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Entities;

[ExcludeFromCodeCoverage]

public abstract class TemplateDbContext(DbContextOptions options) : IdentityDbContext<User>(options), ICanMigrate
{
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeImage> RecipeImages => Set<RecipeImage>();
    public DbSet<RecipeFavorite> RecipeFavorites => Set<RecipeFavorite>();
    public DbSet<RecipeLike> RecipeLikes => Set<RecipeLike>();
    public DbSet<RecipeComment> RecipeComments => Set<RecipeComment>();
    public DbSet<LocalizedRecipe> LocalizedRecipes => Set<LocalizedRecipe>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<RecipeFavorite>()
            .HasKey(f => new { f.UserId, f.RecipeId });

        builder.Entity<RecipeLike>()
            .HasKey(l => new { l.UserId, l.RecipeId });

        builder.Entity<RecipeComment>()
            .HasOne(c => c.ParentComment)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentCommentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<LocalizedRecipe>()
            .HasIndex(lr => new { lr.RecipeId, lr.Culture })
            .IsUnique();
    }

    public virtual  Task MigrateAsync(CancellationToken cancellationToken) =>
        Database.MigrateAsync(cancellationToken);

    public virtual  Task<bool> CanConnectAsync() =>
        Database.CanConnectAsync();
}
