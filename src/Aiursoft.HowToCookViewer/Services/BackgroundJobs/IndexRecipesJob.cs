using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// Scans the local HowToCook repo, parses all recipe Markdown files,
/// copies images into StorageService Workspace, and upserts records into the database.
/// This job should run after SyncHowToCookRepoJob has completed (20-minute start delay).
/// </summary>
public partial class IndexRecipesJob(
    StorageRootPathProvider storageRootPathProvider,
    FeatureFoldersProvider featureFoldersProvider,
    TemplateDbContext db,
    ILogger<IndexRecipesJob> logger) : IBackgroundJob
{
    public string Name => "Index HowToCook Recipes";

    public string Description =>
        "Parses all HowToCook recipe Markdown files and upserts them (with images) into the database.";

    public async Task ExecuteAsync()
    {
        var repoPath = Path.Combine(storageRootPathProvider.GetStorageRootPath(), "repo");
        var dishesPath = Path.Combine(repoPath, "dishes");

        if (!Directory.Exists(dishesPath))
        {
            logger.LogWarning("IndexRecipesJob: dishes directory not found at '{DishesPath}'. " +
                              "Skipping — SyncHowToCookRepoJob may not have run yet.", dishesPath);
            return;
        }

        var markdownFiles = Directory.GetFiles(dishesPath, "*.md", SearchOption.AllDirectories);
        logger.LogInformation("IndexRecipesJob: found {Count} markdown files.", markdownFiles.Length);

        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var validFilePaths = new HashSet<string>();

        foreach (var absoluteFilePath in markdownFiles)
        {
            // Repo-relative path used as the natural key, e.g. "dishes/vegetable_dish/西红柿炒鸡蛋.md"
            var relativeFilePath = Path.GetRelativePath(repoPath, absoluteFilePath)
                .Replace('\\', '/');

            validFilePaths.Add(relativeFilePath);

            try
            {
                var lastModified = await GetGitLastModifiedAsync(repoPath, relativeFilePath);
                var existing = await db.Recipes
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.FilePath == relativeFilePath);

                if (existing != null && existing.FileLastModified == lastModified && !existing.IsDeleted)
                {
                    // Also verify all referenced image files still exist on disk.
                    // If any are missing (e.g. Workspace was wiped), fall through and re-process.
                    var workspaceRoot = featureFoldersProvider.GetWorkspaceFolder();
                    var existingImages = await db.RecipeImages
                        .AsNoTracking()
                        .Where(i => i.RecipeId == existing.Id)
                        .ToListAsync();
                    var allImagesPresent = existingImages.Count == 0 ||
                        existingImages.All(i => File.Exists(Path.Combine(workspaceRoot, i.LogicalPath)));
                    if (allImagesPresent)
                    {
                        skipped++;
                        continue;
                    }
                    logger.LogInformation(
                        "IndexRecipesJob: image files missing for '{File}', re-processing.", relativeFilePath);
                }

                var markdown = await File.ReadAllTextAsync(absoluteFilePath);
                var parsed = ParseRecipe(relativeFilePath, markdown);

                // Copy images from repo dir to StorageService Workspace
                var recipeDir = Path.GetDirectoryName(absoluteFilePath)!;
                var imageLogicalPaths = CopyImages(recipeDir, parsed.ImageFileNames);

                if (existing == null)
                {
                    var recipe = new Recipe
                    {
                        Name = parsed.Name,
                        Category = parsed.Category,
                        GroupName = parsed.GroupName,
                        FilePath = relativeFilePath,
                        Difficulty = parsed.Difficulty,
                        Description = parsed.Description,
                        Ingredients = parsed.Ingredients,
                        Calculation = parsed.Calculation,
                        Steps = parsed.Steps,
                        Notes = parsed.Notes,
                        FileLastModified = lastModified,
                        Images = BuildImageEntities(imageLogicalPaths)
                    };
                    db.Recipes.Add(recipe);
                    inserted++;
                }
                else
                {
                    var recipe = await db.Recipes
                        .Include(r => r.Images)
                        .FirstAsync(r => r.FilePath == relativeFilePath);

                    recipe.Name = parsed.Name;
                    recipe.Category = parsed.Category;
                    recipe.GroupName = parsed.GroupName;
                    recipe.Difficulty = parsed.Difficulty;
                    recipe.Description = parsed.Description;
                    recipe.Ingredients = parsed.Ingredients;
                    recipe.Calculation = parsed.Calculation;
                    recipe.Steps = parsed.Steps;
                    recipe.Notes = parsed.Notes;
                    recipe.FileLastModified = lastModified;
                    recipe.IsDeleted = false;

                    db.RecipeImages.RemoveRange(recipe.Images);
                    recipe.Images = BuildImageEntities(imageLogicalPaths);
                    updated++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "IndexRecipesJob: failed to process '{File}'.", relativeFilePath);
            }
        }

        var allDbRecipes = await db.Recipes
            .IgnoreQueryFilters()
            .Where(r => !r.IsDeleted)
            .ToListAsync();

        var deletedCount = 0;
        foreach (var r in allDbRecipes)
        {
            if (!validFilePaths.Contains(r.FilePath))
            {
                r.IsDeleted = true;
                deletedCount++;
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation(
            "IndexRecipesJob complete: {Inserted} inserted, {Updated} updated, {Skipped} skipped, {Deleted} deleted.",
            inserted, updated, skipped, deletedCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Parsing
    // ─────────────────────────────────────────────────────────────────────────

    private static ParsedRecipe ParseRecipe(string relativeFilePath, string markdown)
    {
        // "dishes/vegetable_dish/西红柿炒鸡蛋.md"  →  category = "vegetable_dish"
        var parts = relativeFilePath.Split('/');
        var category = parts.Length >= 2 ? parts[1] : "unknown";

        // Folder-type: dishes/vegetable_dish/鸡蛋羹/鸡蛋羹.md  → groupName = "鸡蛋羹"
        var groupName = parts.Length >= 4 ? parts[2] : null;

        // Name = markdown file name without extension
        var name = Path.GetFileNameWithoutExtension(parts[^1]);

        // Collect image references before stripping them
        var imageFileNames = ImageRefRegex().Matches(markdown)
            .Select(m => m.Groups["file"].Value)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct()
            .ToList();

        // Strip all image tags from the stored markdown
        var stripped = ImageRefRegex().Replace(markdown, string.Empty);

        var lines = stripped.Split('\n');

        var difficulty = 0;
        var descriptionLines = new List<string>();
        var ingredients = new List<string>();
        var calculation = new List<string>();
        var steps = new List<string>();
        var notes = new List<string>();

        string? currentSection = null;
        var foundDifficulty = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            // Skip the H1 title line ("# 名称")
            if (line.StartsWith("# "))
            {
                continue;
            }

            // Section headers
            if (line.StartsWith("## "))
            {
                currentSection = line[3..].Trim();
                continue;
            }

            // Difficulty line anywhere: "预估烹饪难度：★★★"
            if (!foundDifficulty && line.Contains('★'))
            {
                difficulty = line.Count(c => c == '★');
                foundDifficulty = true;
                continue;
            }

            switch (currentSection)
            {
                case null:
                    // Before the first section: description
                    descriptionLines.Add(line);
                    break;
                case "必备原料和工具":
                    ingredients.Add(line);
                    break;
                case "计算":
                    calculation.Add(line);
                    break;
                case "操作":
                    steps.Add(line);
                    break;
                case "附加内容":
                    notes.Add(line);
                    break;
            }
        }

        return new ParsedRecipe(
            Name: name,
            Category: category,
            GroupName: groupName,
            Difficulty: difficulty,
            Description: string.Join('\n', descriptionLines).Trim(),
            Ingredients: string.Join('\n', ingredients).Trim(),
            Calculation: string.Join('\n', calculation).Trim(),
            Steps: string.Join('\n', steps).Trim(),
            Notes: string.Join('\n', notes).Trim(),
            ImageFileNames: imageFileNames
        );
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Image handling
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies image files from the recipe directory into the StorageService Workspace.
    /// Returns the logical path (e.g. "recipe-images/{uuid}.jpg") for each copied image,
    /// preserving original order so the first entry can be marked as cover.
    /// </summary>
    private List<string> CopyImages(string recipeDir, IEnumerable<string> imageFileNames)
    {
        var logicalPaths = new List<string>();
        var workspaceRoot = featureFoldersProvider.GetWorkspaceFolder();

        foreach (var imageFileName in imageFileNames)
        {
            // Image paths in Markdown are relative to the Markdown file, e.g. "./西红柿炒鸡蛋.jpg"
            var cleanFileName = imageFileName.TrimStart('.', '/', '\\');
            var sourcePath = Path.Combine(recipeDir, cleanFileName);

            if (!File.Exists(sourcePath))
            {
                logger.LogWarning("IndexRecipesJob: image not found at '{Source}'. Skipping.", sourcePath);
                continue;
            }

            var ext = Path.GetExtension(cleanFileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";

            var uuid = Guid.NewGuid().ToString("N");
            var logicalPath = $"recipe-images/{uuid}{ext}";
            var physicalPath = Path.GetFullPath(Path.Combine(workspaceRoot, logicalPath));

            var dir = Path.GetDirectoryName(physicalPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.Copy(sourcePath, physicalPath, overwrite: true);
            logicalPaths.Add(logicalPath);
        }

        return logicalPaths;
    }

    private static List<RecipeImage> BuildImageEntities(List<string> logicalPaths)
    {
        var lastIndex = logicalPaths.Count - 1;
        return logicalPaths
            .Select((path, index) => new RecipeImage
            {
                LogicalPath = path,
                IsCover = index == lastIndex
            })
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Git helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<DateTime> GetGitLastModifiedAsync(string repoPath, string relativeFilePath)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"log -1 --format=%cI -- \"{relativeFilePath}\"",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (DateTimeOffset.TryParse(output.Trim(), out var dt))
        {
            return dt.UtcDateTime;
        }

        logger.LogWarning(
            "IndexRecipesJob: could not parse git log date for '{File}'. Using UtcNow.",
            relativeFilePath);
        return DateTime.UtcNow;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Regex
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Matches Markdown image references: ![alt](./filename.jpg)</summary>
    [GeneratedRegex(@"!\[[^\]]*\]\((?<file>[^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex ImageRefRegex();

    // ─────────────────────────────────────────────────────────────────────────
    // Internal DTO
    // ─────────────────────────────────────────────────────────────────────────

    private record ParsedRecipe(
        string Name,
        string Category,
        string? GroupName,
        int Difficulty,
        string Description,
        string Ingredients,
        string Calculation,
        string Steps,
        string Notes,
        List<string> ImageFileNames
    );
}
