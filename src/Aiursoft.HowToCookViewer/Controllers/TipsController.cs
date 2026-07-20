using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.TipsViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.WebTools.Attributes;
using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Controllers;

[LimitPerMin]
public class TipsController(
    TemplateDbContext db) : Controller
{
    private const string GitHubBaseUrl = "https://github.com/Anduin2017/HowToCook/blob/master/";

    public async Task<IActionResult> Detail(int id)
    {
        var tip = await db.Tips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (tip == null)
            return NotFound();

        var currentCulture = HttpContext.Features
            .Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>()
            ?.RequestCulture.Culture.Name ?? string.Empty;

        var localized = await db.LocalizedTips
            .AsNoTracking()
            .FirstOrDefaultAsync(lt => lt.TipId == id && lt.Culture == currentCulture);

        var displayTitle = !string.IsNullOrWhiteSpace(localized?.LocalizedTitle) ? localized.LocalizedTitle : tip.Title;
        var contentToRender = !string.IsNullOrWhiteSpace(localized?.LocalizedContent) ? localized.LocalizedContent : tip.Content;

        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseMermaid().Build();
        var renderedHtml = Markdown.ToHtml(contentToRender, pipeline);

        return this.StackView(new DetailViewModel
        {
            PageTitle = "Tip",
            Tip = tip,
            RenderedContent = renderedHtml,
            DisplayTitle = displayTitle,
            GitHubEditUrl = GitHubBaseUrl + tip.FilePath
        });
    }
}
