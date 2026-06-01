using System.Text;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.IngredientsViewModels;
using Aiursoft.HowToCookViewer.Util;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.HowToCookViewer.Services;

public class IngredientGroupService(
    IHttpClientFactory httpClientFactory,
    ILogger<IngredientGroupService> logger)
{
    private readonly object _lock = new();
    private SemaphoreSlim? _rebuildSemaphore;
    private List<IngredientGroupViewModel>? _cachedGroups;
    private Dictionary<int, int[]>? _canonicalToAliasIds;
    private (int Count, int MaxId, int Threshold) _snapshot;

    private SemaphoreSlim GetRebuildSemaphore()
    {
        if (_rebuildSemaphore != null) return _rebuildSemaphore;
        lock (_lock)
        {
            return _rebuildSemaphore ??= new SemaphoreSlim(1, 1);
        }
    }

    public async Task<IReadOnlyList<IngredientGroupViewModel>> GetGroupsAsync(
        TemplateDbContext db, GlobalSettingsService settingsService)
    {
        var currentSnapshot = await GetSnapshotAsync(db, settingsService);

        if (_cachedGroups != null && _snapshot == currentSnapshot)
            return _cachedGroups;

        var semaphore = GetRebuildSemaphore();
        await semaphore.WaitAsync();
        try
        {
            currentSnapshot = await GetSnapshotAsync(db, settingsService);
            if (_cachedGroups != null && _snapshot == currentSnapshot)
                return _cachedGroups;

            var groups = await BuildGroupsAsync(db, settingsService, currentSnapshot.Threshold);
            _snapshot = currentSnapshot;
            _cachedGroups = groups;
            return _cachedGroups;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public int[] ExpandCanonicalIds(int[] canonicalIds)
    {
        if (_canonicalToAliasIds == null) return canonicalIds;
        var result = new HashSet<int>();
        foreach (var id in canonicalIds)
        {
            result.Add(id);
            if (_canonicalToAliasIds.TryGetValue(id, out var aliases))
                foreach (var alias in aliases)
                    result.Add(alias);
        }
        return [.. result];
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _cachedGroups = null;
            _canonicalToAliasIds = null;
        }
    }

    private static async Task<(int Count, int MaxId, int Threshold)> GetSnapshotAsync(
        TemplateDbContext db, GlobalSettingsService settingsService)
    {
        var count = await db.Ingredients.CountAsync();
        var maxId = await db.Ingredients.MaxAsync(i => (int?)i.Id) ?? 0;
        var thresholdStr = await settingsService.GetSettingValueAsync(SettingsMap.IngredientSimilarityThreshold);
        var threshold = int.TryParse(thresholdStr, out var t) ? t : 80;
        return (count, maxId, threshold);
    }

    private async Task<List<IngredientGroupViewModel>> BuildGroupsAsync(
        TemplateDbContext db, GlobalSettingsService settingsService, int thresholdPercent)
    {
        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        var instance = await GetEmbeddingInstanceAsync(settingsService);
        var token = await GetEmbeddingTokenAsync(settingsService);
        var canEmbed = !string.IsNullOrWhiteSpace(instance) && !string.IsNullOrWhiteSpace(model);
        var threshold = thresholdPercent / 100.0;

        var ingredients = await db.Ingredients
            .Include(i => i.Recipes)
            .OrderBy(i => i.Id)
            .ToListAsync();

        if (ingredients.Count == 0) return [];

        if (canEmbed)
            await GenerateMissingEmbeddingsAsync(db, instance, model, token, ingredients);

        float[]?[] embeddings = new float[ingredients.Count][];
        for (var i = 0; i < ingredients.Count; i++)
        {
            var vec = EmbeddingHelper.Deserialize(ingredients[i].Embedding);
            if (vec != null) embeddings[i] = vec;
        }

        var (groups, aliasIds) = ClusterAndBuildGroups(ingredients, embeddings, threshold);

        foreach (var group in groups)
        {
            foreach (var alias in group.Aliases)
                alias.CanonicalIngredientId = group.Canonical.Id;
            group.Canonical.CanonicalIngredientId = null;
        }
        await db.SaveChangesAsync();

        groups = groups
            .OrderByDescending(g => g.DistinctRecipeCount)
            .ThenBy(g => g.Canonical.Name)
            .ToList();

        _canonicalToAliasIds = aliasIds;
        logger.LogInformation(
            "IngredientGroupService: Rebuilt {GroupCount} groups from {IngredientCount} ingredients (threshold={Threshold}%)",
            groups.Count, ingredients.Count, thresholdPercent);

        return groups;
    }

    private async Task GenerateMissingEmbeddingsAsync(
        TemplateDbContext db, string instance, string model, string token, List<Ingredient> ingredients)
    {
        var missing = ingredients
            .Where(i => i.Embedding == null)
            .ToList();

        if (missing.Count == 0) return;

        try
        {
            var names = missing.Select(i => i.Name).ToArray();
            var vectors = await EmbedTextsAsync(instance, model, token, names);
            for (var i = 0; i < missing.Count; i++)
            {
                missing[i].Embedding = EmbeddingHelper.Serialize(vectors[i]);
                missing[i].LastEmbeddedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to batch-generate embeddings for {Count} ingredients", missing.Count);
        }
    }

    private static (List<IngredientGroupViewModel> Groups, Dictionary<int, int[]> AliasIds) ClusterAndBuildGroups(
        List<Ingredient> ingredients, float[]?[] embeddings, double threshold)
    {
        var dsu = new DisjointSetUnion(ingredients.Count);
        for (var i = 0; i < ingredients.Count; i++)
        {
            var embI = embeddings[i];
            if (embI == null) continue;
            for (var j = i + 1; j < ingredients.Count; j++)
            {
                var embJ = embeddings[j];
                if (embJ == null) continue;
                if (EmbeddingHelper.CosineSimilarity(embI, embJ) >= threshold)
                    dsu.Union(i, j);
            }
        }

        var rawGroups = dsu.AsGroups(ignoreSingletons: false);
        var groups = new List<IngredientGroupViewModel>();
        var aliasIds = new Dictionary<int, int[]>();

        foreach (var indices in rawGroups)
        {
            var members = indices.Select(idx => ingredients[idx]).ToList();
            var canonical = members
                .OrderBy(m => m.Name.Length)
                .ThenBy(m => m.Id)
                .First();
            var aliases = members.Where(m => m.Id != canonical.Id).ToList();
            var distinctRecipeIds = members
                .SelectMany(m => m.Recipes.Select(r => r.Id))
                .ToHashSet();

            groups.Add(new IngredientGroupViewModel
            {
                Canonical = canonical,
                Aliases = aliases,
                DistinctRecipeCount = distinctRecipeIds.Count
            });

            aliasIds[canonical.Id] = aliases.Select(a => a.Id).ToArray();
        }

        return (groups, aliasIds);
    }

    private async Task<List<float[]>> EmbedTextsAsync(string instance, string model, string token, string[] texts)
    {
        var http = httpClientFactory.CreateClient();
        var baseUri = new Uri(instance);
        var embedEndpoint = $"{baseUri.Scheme}://{baseUri.Authority}/api/embed?keep_alive=-1";
        var requestBody = new { model, input = texts, options = new { num_gpu = 0 } };
        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, embedEndpoint) { Content = content };
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var response = await http.SendAsync(request, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(timeoutCts.Token);
        if (result?.Embeddings == null || result.Embeddings.Length == 0)
            throw new Exception("Ollama returned no embeddings.");

        foreach (var vector in result.Embeddings)
            EmbeddingHelper.Normalize(vector);
        return [.. result.Embeddings];
    }

    private async Task<string> GetEmbeddingInstanceAsync(GlobalSettingsService settingsService)
    {
        var dedicated = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingOllamaInstance);
        return !string.IsNullOrWhiteSpace(dedicated)
            ? dedicated
            : await settingsService.GetSettingValueAsync(SettingsMap.OpenAiInstance);
    }

    private async Task<string> GetEmbeddingTokenAsync(GlobalSettingsService settingsService)
    {
        var dedicated = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingApiToken);
        return !string.IsNullOrWhiteSpace(dedicated)
            ? dedicated
            : await settingsService.GetSettingValueAsync(SettingsMap.OpenAiApiToken);
    }

    private class OllamaEmbedResponse
    {
        [JsonProperty("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
