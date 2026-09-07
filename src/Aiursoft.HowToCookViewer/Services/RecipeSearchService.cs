using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services;

/// <summary>Weighted keyword search with database scoring and pagination for every term count.</summary>
public static class RecipeSearchService
{
    public static async Task<(List<Recipe> Items, int TotalCount)> SearchAsync(
        IQueryable<Recipe> baseQuery,
        TemplateDbContext db,
        string keyword,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var terms = SplitTerms(keyword);
        if (terms.Length == 0) return ([], 0);

        var ordered = BuildRankedQuery(baseQuery, db, terms);
        var total = await ordered.CountAsync(ct);
        var items = await ordered
            .Skip((Math.Clamp(page, 1, int.MaxValue / pageSize) - 1) * pageSize)
            .Take(pageSize)
            .SelectCards()
            .ToListAsync(ct);
        return (items, total);
    }

    internal static IQueryable<Recipe> BuildRankedQuery(IQueryable<Recipe> baseQuery, TemplateDbContext db, string[] terms)
    {
        var parameter = Expression.Parameter(typeof(Recipe), "r");
        Expression match = Expression.Constant(false);
        Expression score = Expression.Constant(0);
        foreach (var term in terms)
        {
            var lower = term.ToLowerInvariant();
            Expression<Func<Recipe, bool>> termMatch = r =>
                r.Name.ToLower().Contains(lower) || r.Description.ToLower().Contains(lower) ||
                r.LocalizedRecipes.Any(lr => lr.LocalizedName.ToLower().Contains(lower) ||
                                            lr.LocalizedDescription.ToLower().Contains(lower));
            Expression<Func<Recipe, int>> termScore = r =>
                (r.Name.ToLower() == lower ? 1000 : 0)
                + (r.Name.ToLower().StartsWith(lower) ? 100 : 0)
                + (r.Name.ToLower().Contains(lower) ? 10 : 0)
                + (r.Description.ToLower().Contains(lower) ? 1 : 0)
                + (r.LocalizedRecipes.Any(lr => lr.LocalizedName.ToLower() == lower) ? 1000 : 0)
                + (r.LocalizedRecipes.Any(lr => lr.LocalizedName.ToLower().StartsWith(lower)) ? 100 : 0)
                + (r.LocalizedRecipes.Any(lr => lr.LocalizedName.ToLower().Contains(lower)) ? 10 : 0)
                + (r.LocalizedRecipes.Any(lr => lr.LocalizedDescription.ToLower().Contains(lower)) ? 1 : 0);
            match = Expression.OrElse(match, new ReplaceParameter(termMatch.Parameters[0], parameter).Visit(termMatch.Body));
            score = Expression.Add(score, new ReplaceParameter(termScore.Parameters[0], parameter).Visit(termScore.Body));
        }

        var filtered = baseQuery.Where(Expression.Lambda<Func<Recipe, bool>>(match, parameter));
        return filtered
            .OrderByDescending(Expression.Lambda<Func<Recipe, int>>(score, parameter))
            .ThenByDescending(r => r.Images.Any())
            .ThenByDescending(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id))
            .ThenByDescending(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id))
            .ThenBy(r => r.Name)
            .ThenBy(r => r.Id);
    }

    private sealed class ReplaceParameter(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : base.VisitParameter(node);
    }

    public static string[] SplitTerms(string keyword) =>
        Regex.Split(keyword.Trim(), @"\s+").Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
}
