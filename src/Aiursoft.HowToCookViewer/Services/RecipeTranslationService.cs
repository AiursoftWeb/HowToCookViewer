using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.HowToCookViewer.Services;

/// <summary>
/// Calls an OpenAI-compatible chat completions endpoint (Ollama or DeepSeek)
/// to translate a piece of text into a target language.
/// Settings (instance, model, token) are read at call time so admin changes take effect immediately.
/// </summary>
public class RecipeTranslationService(
    IHttpClientFactory httpClientFactory,
    ILogger<RecipeTranslationService> logger) : IScopedDependency
{
    public async Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        string instance,
        string model,
        string token)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var prompt = $"Translate the following text to {targetLanguage}. Preserve all markdown formatting exactly. Output only the translation, no explanations, no preamble:\n\n{text}";

        var requestBody = new
        {
            model,
            stream = false,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        logger.LogDebug("Translating {Chars} chars to {Language} via {Instance}", text.Length, targetLanguage, instance);

        var response = await client.PostAsync(instance, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        // OpenAI-compatible format: { "choices": [{ "message": { "content": "..." } }] }
        if (root.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var choiceMsg) &&
            choiceMsg.TryGetProperty("content", out var choiceContent))
        {
            return choiceContent.GetString() ?? text;
        }

        // Native Ollama format: { "message": { "content": "..." } }
        if (root.TryGetProperty("message", out var ollamaMsg) &&
            ollamaMsg.TryGetProperty("content", out var ollamaContent))
        {
            return ollamaContent.GetString() ?? text;
        }

        logger.LogWarning("RecipeTranslationService: unrecognized response shape. Raw: {Json}",
            responseJson.Length > 500 ? responseJson[..500] : responseJson);
        return text;
    }
}
