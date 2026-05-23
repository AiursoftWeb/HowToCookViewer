using Aiursoft.Canon;
using Aiursoft.Dotlang.Shared;
using Aiursoft.GptClient.Services;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.HowToCookViewer.Services;

/// <summary>
/// Translates text using Dotlang's <see cref="OllamaBasedTranslatorEngine"/>.
/// Ollama settings (instance, model, token) are read from <see cref="GlobalSettingsService"/>
/// at call time so admin changes take effect immediately.
/// </summary>
public class RecipeTranslationService(
    GlobalSettingsService settingsService,
    MarkdownShredder shredder,
    RetryEngine retryEngine,
    ILogger<OllamaBasedTranslatorEngine> engineLogger,
    ChatClient chatClient) : IScopedDependency
{
    public async Task<string> TranslateAsync(string text, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var options = Options.Create(new TranslateOptions
        {
            OllamaInstance = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiInstance),
            OllamaModel = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiLocalizationModel),
            OllamaToken = await settingsService.GetSettingValueAsync(SettingsMap.OpenAiApiToken)
        });

        var engine = new OllamaBasedTranslatorEngine(options, retryEngine, engineLogger, chatClient, shredder);
        return await engine.TranslateAsync(text, targetLanguage);
    }
}
