using Aiursoft.Scanner.Abstractions;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace Aiursoft.HowToCookViewer.Services;

public class GlobalSettingsService(
    TemplateDbContext dbContext,
    IConfiguration configuration,
    StorageService storageService,
    IMemoryCache cache,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<GlobalSettingsService> logger) : IScopedDependency
{
    private const string ProtectedValuePrefix = "protected:v1:";
    private readonly IDataProtector _secretProtector = dataProtectionProvider.CreateProtector("GlobalSettings/Secrets/v1");

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
        var definition = SettingsMap.Definitions.FirstOrDefault(d => d.Key == key);
        string result;
        if (dbSetting != null && dbSetting.Value != null)
        {
            result = definition?.Type == SettingType.Secret
                ? UnprotectSecret(key, dbSetting.Value)
                : dbSetting.Value;
        }
        else
        {
            // 3. Fallback to default
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
        var instance = await GetSettingValueAsync(SettingsMap.OpenAiInstance);
        return !string.IsNullOrWhiteSpace(instance);
    }

    public async Task<bool> IsAiSearchEnabledAsync()
    {
        var dedicated = await GetSettingValueAsync(SettingsMap.EmbeddingOllamaInstance);
        if (!string.IsNullOrWhiteSpace(dedicated)) return true;

        var instance = await GetSettingValueAsync(SettingsMap.OpenAiInstance);
        return !string.IsNullOrWhiteSpace(instance);
    }

    public async Task<string> GetEmbeddingEndpointAsync()
    {
        var dedicated = await GetSettingValueAsync(SettingsMap.EmbeddingOllamaInstance);
        if (!string.IsNullOrWhiteSpace(dedicated)) return dedicated.TrimEnd('/');

        var openAi = await GetSettingValueAsync(SettingsMap.OpenAiInstance);
        if (!string.IsNullOrWhiteSpace(openAi))
        {
            var uri = new Uri(openAi);
            return $"{uri.Scheme}://{uri.Authority}";
        }

        return string.Empty;
    }

    public async Task<string> GetEmbeddingTokenAsync()
    {
        var dedicated = await GetSettingValueAsync(SettingsMap.EmbeddingApiToken);
        if (!string.IsNullOrWhiteSpace(dedicated)) return dedicated;

        return await GetSettingValueAsync(SettingsMap.OpenAiApiToken);
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

        var storedValue = ProtectSecretIfNeeded(definition, value);
        var dbSetting = await dbContext.GlobalSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (dbSetting == null)
        {
            dbSetting = new GlobalSetting { Key = key, Value = storedValue };
            dbContext.GlobalSettings.Add(dbSetting);
        }
        else
        {
            dbSetting.Value = storedValue;
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
                // Configuration overrides are read directly and must not be copied into the database.
                var initialValue = definition.Type == SettingType.Secret
                    ? definition.DefaultValue
                    : configuration[$"GlobalSettings:{definition.Key}"]
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

    private string ProtectSecretIfNeeded(GlobalSettingDefinition definition, string value)
    {
        if (definition.Type != SettingType.Secret || string.IsNullOrEmpty(value))
        {
            return value;
        }

        return ProtectedValuePrefix + _secretProtector.Protect(value);
    }

    private string UnprotectSecret(string key, string storedValue)
    {
        // Backward compatibility for secrets saved before encrypted settings existed.
        if (!storedValue.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal))
        {
            return storedValue;
        }

        try
        {
            return _secretProtector.Unprotect(storedValue[ProtectedValuePrefix.Length..]);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex, "Could not decrypt secret global setting '{SettingKey}'", key);
            return string.Empty;
        }
    }
}
