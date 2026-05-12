using Aiursoft.Canon;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.GptClient.Services;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// Periodically extracts standardized ingredient names from recipe content using an AI endpoint.
/// Saves ingredients to the database and links them to the recipe for future reverse-search capabilities.
/// </summary>
public class ExtractIngredientsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    ChatClient chatClient,
    RetryEngine retryEngine,
    ILogger<ExtractIngredientsJob> logger) : IBackgroundJob
{
    public string Name => "Extract Recipe Ingredients";

    public string Description =>
        "Extracts standardized, simple Chinese ingredient nouns from recipes using an AI endpoint.";

    public async Task ExecuteAsync()
    {
        var instance = await settingsService.GetSettingValueAsync(SettingsMap.OllamaInstance);
        var model = await settingsService.GetSettingValueAsync(SettingsMap.OllamaModel);

        if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(model))
        {
            logger.LogInformation("ExtractIngredientsJob: Ollama endpoint or model not configured. Skipping.");
            return;
        }

        var lastId = 0;
        while (true)
        {
            var currentLastId = lastId;
            var pendingRecipes = await db.Recipes
                .Where(r => r.Id > currentLastId && r.LastIngredientExtractedAt < r.FileLastModified)
                .OrderBy(r => r.Id)
                .Take(10)
                .ToListAsync();

            if (pendingRecipes.Count == 0) break;

            foreach (var recipe in pendingRecipes)
            {
                try
                {
                    await retryEngine.RunWithRetry(async _ =>
                    {
                        await ExtractForRecipeAsync(recipe, instance, model);
                        recipe.LastIngredientExtractedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "ExtractIngredientsJob: Failed to extract ingredients for recipe '{Name}' after retries.", recipe.Name);
                }
            }

            lastId = pendingRecipes.Max(r => r.Id);
        }
        }

        private async Task ExtractForRecipeAsync(Recipe recipe, string instance, string model)
        {
        if (string.IsNullOrWhiteSpace(recipe.Ingredients))
        {
            logger.LogInformation("ExtractIngredientsJob: Recipe '{Name}' has no ingredients content. Skipping.", recipe.Name);
            return;
        }

        var prompt = $"""
            你是一个专业的厨师和数据分析师。请从提供的菜谱内容（Markdown格式）中提取出所有的原料名词。不包含厨具、工具、数量、单位、形容词或任何非原料的词汇。
            
            要求：
            1. 只提取最单纯的精准的名词，去除所有形容词（如“生”、“熟”、“可选”、“大约”、“克”、“毫升”、“勺”等）。
            2. 将名称标准化，例如“蛋”、“生鸡蛋”统一为“鸡蛋”。
            3. 结果必须是标准的 JSON 数组，包含字符串。例如：["鸡蛋", "西红柿"]
            4. 不要包含任何额外的描述、解释或 Markdown 格式（如 ```json 代码块）。
            5. 必须是中文名词。
            6. 必须是必备的材料。如果是可选的材料，请不要提取！请忽视一切可选的材料！
            7. 复合材料（包含多种配料的材料）请提取最终材料的名称。例如“复配食品增稠剂（κ-卡拉胶 45%、瓜尔胶 35%、氯化钾 20%）”提取为“食品增稠剂”。

            菜谱内容：
            {recipe.Ingredients}
            """;

        var token = await settingsService.GetSettingValueAsync(SettingsMap.OllamaToken);
        var response = await chatClient.AskString(model, instance, token, new[] { prompt }, CancellationToken.None);

        var json = response.GetFullContent().Trim();
        if (json.StartsWith("```json")) json = json[7..].Trim();
        if (json.StartsWith("```")) json = json[3..].Trim();
        if (json.EndsWith("```")) json = json[..^3].Trim();

        string[]? ingredients;
        try 
        {
            ingredients = JsonConvert.DeserializeObject<string[]>(json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExtractIngredientsJob: Failed to parse JSON from Ollama for recipe '{Name}'. Response: {Response}", recipe.Name, response.GetFullContent());
            throw; // Trigger retry
        }

        if (ingredients == null)
        {
            throw new Exception($"Ollama returned null ingredients for recipe '{recipe.Name}'.");
        }

        var standardizedNames = ingredients
            .Select(i => i.Trim())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct()
            .ToList();

        // Load existing recipe with its ingredients
        var recipeWithIngredients = await db.Recipes
            .Include(r => r.ConsumedIngredients)
            .FirstAsync(r => r.Id == recipe.Id);

        recipeWithIngredients.ConsumedIngredients.Clear();

        foreach (var name in standardizedNames)
        {
            var ingredient = await db.Ingredients.FirstOrDefaultAsync(i => i.Name == name);
            if (ingredient == null)
            {
                ingredient = new Ingredient { Name = name };
                db.Ingredients.Add(ingredient);
                // Save to avoid uniqueness constraint violation if multiple recipes use the same new ingredient in this batch
                await db.SaveChangesAsync();
            }
            recipeWithIngredients.ConsumedIngredients.Add(ingredient);
        }
    }
}
