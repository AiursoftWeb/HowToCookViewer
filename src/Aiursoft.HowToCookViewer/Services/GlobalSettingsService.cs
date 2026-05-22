using Aiursoft.Scanner.Abstractions;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aiursoft.HowToCookViewer.Services;

public class GlobalSettingsService(
    TemplateDbContext dbContext,
    IConfiguration configuration,
    StorageService storageService,
    IMemoryCache cache) : IScopedDependency
{
    private string GetCacheKey(string key) => $"global-setting-{key}";

    public async Task<string> GetSettingValueAsync(string key)
    {
        var cacheKey = GetCacheKey(key);
        if (cache.TryGetValue(cacheKey, out string? cachedValue) && cachedValue != null)
        {
            return cachedValue;
        }

        // 1. Check configuration (Environment variables, appsettings.json, etc.)
        var configValue = configuration[$"GlobalSettings:{key}"] ?? configuration[key];
        if (!string.IsNullOrWhiteSpace(configValue))
        {
            return configValue;
        }

        // 2. Check database
        var dbSetting = await dbContext.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
        string result;
        if (dbSetting != null && dbSetting.Value != null)
        {
            result = dbSetting.Value;
        }
        else
        {
            // 3. Fallback to default
            var definition = SettingsMap.Definitions.FirstOrDefault(d => d.Key == key);
            result = definition?.DefaultValue ?? string.Empty;
        }

        cache.Set(cacheKey, result, TimeSpan.FromHours(2));
        return result;
    }

    public async Task<bool> GetBoolSettingAsync(string key)
    {
        var value = await GetSettingValueAsync(key);
        return bool.TryParse(value, out var result) && result;
    }

    // ReSharper disable once UnusedMember.Global
    public async Task<int> GetIntSettingAsync(string key)
    {
        var value = await GetSettingValueAsync(key);
        return int.TryParse(value, out var result) ? result : 0;
    }

    public bool IsOverriddenByConfig(string key)
    {
        return !string.IsNullOrWhiteSpace(configuration[$"GlobalSettings:{key}"]) ||
               !string.IsNullOrWhiteSpace(configuration[key]);
    }

    public async Task<bool> IsAiLocalizationEnabledAsync()
    {
        var instance = await GetSettingValueAsync(SettingsMap.OllamaInstance);
        return !string.IsNullOrWhiteSpace(instance);
    }

    public async Task<bool> IsAiSearchEnabledAsync()
    {
        var dedicated = await GetSettingValueAsync(SettingsMap.OllamaInstanceForEmbedding);
        if (!string.IsNullOrWhiteSpace(dedicated)) return true;

        var instance = await GetSettingValueAsync(SettingsMap.OllamaInstance);
        return !string.IsNullOrWhiteSpace(instance);
    }

    public async Task UpdateSettingAsync(string key, string value)
    {
        if (IsOverriddenByConfig(key))
        {
            throw new InvalidOperationException($"Setting {key} is overridden by configuration and cannot be updated in database.");
        }

        var definition = SettingsMap.Definitions.FirstOrDefault(d => d.Key == key)
                         ?? throw new InvalidOperationException($"Setting {key} is not defined.");

        // Validation
        switch (definition.Type)
        {
            case SettingType.Bool:
                if (!bool.TryParse(value, out _))
                {
                    throw new InvalidOperationException($"Value '{value}' is not a valid boolean for setting {key}.");
                }
                break;
            case SettingType.Number:
                if (!double.TryParse(value, out _))
                {
                    throw new InvalidOperationException($"Value '{value}' is not a valid number for setting {key}.");
                }
                break;
            case SettingType.Choice:
                if (definition.ChoiceOptions != null && !definition.ChoiceOptions.ContainsKey(value))
                {
                    throw new InvalidOperationException($"Value '{value}' is not a valid choice for setting {key}.");
                }
                break;
            case SettingType.File:
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException($"File path cannot be empty for setting {key}.");
                }
                // Validate that the file exists and path is secure using StorageService
                try
                {
                    var physicalPath = storageService.GetFilePhysicalPath(value, isVault: false);
                    if (!File.Exists(physicalPath))
                    {
                        throw new InvalidOperationException($"File not found for setting {key}.");
                    }
                }
                catch (ArgumentException)
                {
                    throw new InvalidOperationException($"Invalid file path for setting {key}.");
                }
                break;
            case SettingType.Text:
            default:
                break;
        }

        var dbSetting = await dbContext.GlobalSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (dbSetting == null)
        {
            dbSetting = new GlobalSetting { Key = key, Value = value };
            dbContext.GlobalSettings.Add(dbSetting);
        }
        else
        {
            dbSetting.Value = value;
        }

        await dbContext.SaveChangesAsync();
        cache.Remove(GetCacheKey(key));
    }

    public async Task SeedSettingsAsync()
    {
        foreach (var definition in SettingsMap.Definitions)
        {
            var exists = await dbContext.GlobalSettings.AnyAsync(s => s.Key == definition.Key);
            if (!exists)
            {
                var initialValue = configuration[$"GlobalSettings:{definition.Key}"]
                                   ?? configuration[definition.Key]
                                   ?? definition.DefaultValue;
                dbContext.GlobalSettings.Add(new GlobalSetting
                {
                    Key = definition.Key,
                    Value = initialValue
                });
                cache.Remove(GetCacheKey(definition.Key));
            }
        }
        await dbContext.SaveChangesAsync();
    }
}
