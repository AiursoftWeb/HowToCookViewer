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

            var groups = await BuildGroupsAsync(db, settingsService);
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
        TemplateDbContext db, GlobalSettingsService settingsService)
    {
        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        var instance = await GetEmbeddingInstanceAsync(settingsService);
        var token = await GetEmbeddingTokenAsync(settingsService);
        var canEmbed = !string.IsNullOrWhiteSpace(instance) && !string.IsNullOrWhiteSpace(model);

        var thresholdStr = await settingsService.GetSettingValueAsync(SettingsMap.IngredientSimilarityThreshold);
        var threshold = double.TryParse(thresholdStr, out var t) ? t / 100.0 : 0.80;

        var ingredients = await db.Ingredients
            .Include(i => i.Recipes)
            .OrderBy(i => i.Id)
            .ToListAsync();

        if (ingredients.Count == 0) return [];

        // Generate embeddings for ingredients that don't have one (batched)
        if (canEmbed)
        {
            var missing = ingredients
                .Select((ing, idx) => (Ingredient: ing, Index: idx))
                .Where(x => x.Ingredient.Embedding == null)
                .ToList();

            if (missing.Count > 0)
            {
                try
                {
                    var names = missing.Select(x => x.Ingredient.Name).ToArray();
                    var vectors = await EmbedTextsAsync(instance, model, token, names);
                    for (var i = 0; i < missing.Count; i++)
                    {
                        missing[i].Ingredient.Embedding = Serialize(vectors[i]);
                        missing[i].Ingredient.LastEmbeddedAt = DateTime.UtcNow;
                    }
                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to batch-generate embeddings for {Count} ingredients", missing.Count);
                }
            }
        }

        // Deserialize embeddings
        var embeddings = new float[ingredients.Count][];
        for (var i = 0; i < ingredients.Count; i++)
        {
            var vec = Deserialize(ingredients[i].Embedding);
            if (vec != null) embeddings[i] = vec;
        }

        // DSU clustering: O(n²) cosine similarity, fine for ~770 ingredients
        var dsu = new DisjointSetUnion(ingredients.Count);
        for (var i = 0; i < ingredients.Count; i++)
        {
            if (embeddings[i] == null) continue;
            for (var j = i + 1; j < ingredients.Count; j++)
            {
                if (embeddings[j] == null) continue;
                var similarity = CosineSimilarity(embeddings[i], embeddings[j]);
                if (similarity >= threshold)
                    dsu.Union(i, j);
            }
        }

        // Build groups from DSU
        var rawGroups = dsu.AsGroups(ignoreSingletons: false);
        var groupViewModels = new List<IngredientGroupViewModel>();
        var newCanonicalToAliasIds = new Dictionary<int, int[]>();

        foreach (var indices in rawGroups)
        {
            var members = indices.Select(idx => ingredients[idx]).ToList();

            // Canonical = shortest name; tie-break by lowest Id
            var canonical = members
                .OrderBy(m => m.Name.Length)
                .ThenBy(m => m.Id)
                .First();

            var aliases = members.Where(m => m.Id != canonical.Id).ToList();
            var allIds = members.Select(m => m.Id).ToHashSet();

            // Deduplicated recipe count across the group
            var distinctRecipeIds = members
                .SelectMany(m => m.Recipes.Select(r => r.Id))
                .ToHashSet();

            groupViewModels.Add(new IngredientGroupViewModel
            {
                Canonical = canonical,
                Aliases = aliases,
                AllIngredientIds = allIds,
                DistinctRecipeCount = distinctRecipeIds.Count
            });

            // Build alias mapping: canonical → all alias ingredient IDs
            var aliasIds = aliases.Select(a => a.Id).ToArray();
            newCanonicalToAliasIds[canonical.Id] = aliasIds;
        }

        // Persist CanonicalIngredientId assignments
        foreach (var group in groupViewModels)
        {
            foreach (var alias in group.Aliases)
            {
                alias.CanonicalIngredientId = group.Canonical.Id;
            }
            group.Canonical.CanonicalIngredientId = null;
        }
        await db.SaveChangesAsync();

        // Sort by distinct recipe count descending
        groupViewModels = groupViewModels
            .OrderByDescending(g => g.DistinctRecipeCount)
            .ThenBy(g => g.Canonical.Name)
            .ToList();

        _canonicalToAliasIds = newCanonicalToAliasIds;
        logger.LogInformation(
            "IngredientGroupService: Rebuilt {GroupCount} groups from {IngredientCount} ingredients (threshold={Threshold}%)",
            groupViewModels.Count, ingredients.Count, threshold * 100);

        return groupViewModels;
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
            Normalize(vector);
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

    private static float CosineSimilarity(float[] a, float[] b)
    {
        var dot = 0f;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        return dot;
    }

    private static void Normalize(float[] vector)
    {
        var sumSq = 0f;
        for (var i = 0; i < vector.Length; i++)
            sumSq += vector[i] * vector[i];
        var norm = MathF.Sqrt(sumSq);
        if (norm > 0)
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
    }

    private static byte[] Serialize(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[]? Deserialize(byte[]? bytes)
    {
        if (bytes == null || bytes.Length % 4 != 0) return null;
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private class OllamaEmbedResponse
    {
        [JsonProperty("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
