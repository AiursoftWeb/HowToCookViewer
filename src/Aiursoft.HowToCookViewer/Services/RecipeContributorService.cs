using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Models.RecipesViewModels;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.HowToCookViewer.Services;

public class RecipeContributorService(
    StorageRootPathProvider storageRootPathProvider,
    GlobalSettingsService globalSettingsService,
    ILogger<RecipeContributorService> logger) : IScopedDependency
{
    public async Task<List<ContributorViewModel>> GetContributorsAsync(string filePath)
    {
        var repoPath = Path.Combine(storageRootPathProvider.GetStorageRootPath(), "repo");
        var repoUrl = await globalSettingsService.GetSettingValueAsync(SettingsMap.HowToCookRepoUrl);

        // Convert clone URL to web URL: strip .git suffix
        var repoWebUrl = repoUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repoUrl[..^4]
            : repoUrl;

        try
        {
            var output = RunGitLog(repoPath, filePath);
            var contributors = ParseContributors(output, repoWebUrl);
            return contributors;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to get contributors for file {FilePath}", filePath);
            return new List<ContributorViewModel>();
        }
    }

    private string RunGitLog(string repoPath, string filePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"log --pretty=format:\"%aN|%aE\" -- \"{filePath}\"",
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null) return string.Empty;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private List<ContributorViewModel> ParseContributors(string gitOutput, string repoWebUrl)
    {
        var lines = gitOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var contributorMap = new Dictionary<string, (string Name, int Count)>();

        foreach (var line in lines)
        {
            var parts = line.Split('|');
            if (parts.Length < 2) continue;
            var name = parts[0].Trim();
            var email = parts[1].Trim();

            if (contributorMap.TryGetValue(email, out var data))
            {
                contributorMap[email] = (data.Name, data.Count + 1);
            }
            else
            {
                contributorMap[email] = (name, 1);
            }
        }

        return contributorMap.Select(kvp => new ContributorViewModel
        {
            Name = kvp.Value.Name,
            Email = kvp.Key,
            CommitCount = kvp.Value.Count,
            AvatarUrl = GetGravatarUrl(kvp.Key),
            GitHubProfileUrl = GetGitHubProfileUrl(kvp.Key, repoWebUrl)
        })
        .OrderByDescending(c => c.CommitCount)
        .ToList();
    }

    private string GetGravatarUrl(string email)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(email.ToLower().Trim()));
        var hashStr = BitConverter.ToString(hash).Replace("-", "").ToLower();
        return $"https://www.gravatar.com/avatar/{hashStr}?d=identicon";
    }

    private string GetGitHubProfileUrl(string email, string repoWebUrl)
    {
        // If it's a GitHub noreply email, we can get the username
        // username@users.noreply.github.com
        if (email.EndsWith("@users.noreply.github.com", StringComparison.OrdinalIgnoreCase))
        {
            var username = email.Split('@')[0];
            // GitHub noreply emails can be in format id+username@users.noreply.github.com
            if (username.Contains('+'))
            {
                username = username.Split('+')[1];
            }
            return $"https://github.com/{username}";
        }

        // Fallback: search by email on GitHub
        // Or link to the author's commits in this repo
        return $"{repoWebUrl}/commits?author={email}";
    }
}
