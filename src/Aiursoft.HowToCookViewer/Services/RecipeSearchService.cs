using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services;

/// <summary>
/// Weighted relevance search for recipes.
///
/// Scoring weights (per matched term):
///   Exact recipe name match  → 1000
///   Recipe name prefix match → 100
///   Recipe name contains     → 10
///   Description contains     → 1
///
/// Single-term searches are fully translated to SQL (CASE WHEN scoring,
/// ORDER BY score, OFFSET/LIMIT pagination — no data pulled into memory).
/// Multi-term searches use SQL to pre-filter then score in memory.
/// </summary>
[ExcludeFromCodeCoverage]
public static class RecipeSearchService
{
    public static async Task<(List<Recipe> Items, int TotalCount)> SearchAsync(
        IQueryable<Recipe> baseQuery,
        string keyword,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var terms = SplitTerms(keyword);
        if (terms.Length == 0) return ([], 0);

        return terms.Length == 1
            ? await SingleTermSqlSearch(baseQuery, terms[0], page, pageSize, ct)
            : await MultiTermHybridSearch(baseQuery, terms, page, pageSize, ct);
    }

    /// <summary>
    /// Single-term path: scoring expression is fully pushed to SQL.
    /// </summary>
    private static async Task<(List<Recipe> Items, int TotalCount)> SingleTermSqlSearch(
        IQueryable<Recipe> baseQuery,
        string term,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var termLower = term.ToLower();
        var scoreQuery = baseQuery
            .Where(r => r.Name.Contains(term) || r.Description.Contains(term))
            .Select(r => new
            {
                Recipe = r,
                Score =
                    (r.Name.ToLower() == termLower ? 1000 : 0)
                    + (r.Name.StartsWith(term) ? 100 : 0)
                    + (r.Name.Contains(term) ? 10 : 0)
                    + (r.Description.Contains(term) ? 1 : 0)
            });

        var ordered = scoreQuery
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Recipe.Name)
            .Select(x => x.Recipe);

        var total = await ordered.CountAsync(ct);
        var items = await ordered
            .Include(r => r.Images)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <summary>
    /// Multi-term path: SQL filters candidates, then in-memory scoring.
    /// </summary>
    private static async Task<(List<Recipe> Items, int TotalCount)> MultiTermHybridSearch(
        IQueryable<Recipe> baseQuery,
        string[] terms,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var filtered = await baseQuery
            .Where(r => terms.Any(t => r.Name.Contains(t))
                     || terms.Any(t => r.Description.Contains(t)))
            .Include(r => r.Images)
            .AsNoTracking()
            .ToListAsync(ct);

        var ordered = filtered
            .Select(r => (Recipe: r, Score: ComputeScore(r, terms)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Recipe.Name)
            .Select(x => x.Recipe)
            .ToList();

        var total = ordered.Count;
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (items, total);
    }

    private static int ComputeScore(Recipe r, string[] terms) =>
        terms.Sum(term =>
            (r.Name.Equals(term, StringComparison.OrdinalIgnoreCase) ? 1000 : 0)
            + (r.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 100 : 0)
            + (r.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 10 : 0)
            + (r.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0));

    public static string[] SplitTerms(string keyword) =>
        Regex.Split(keyword.Trim(), @"\s+")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();
}
