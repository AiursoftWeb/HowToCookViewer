using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Aiursoft.HowToCookViewer.Services;

public static class MermaidExtensionMethods
{
    /// <summary>
    /// Adds support for rendering mermaid fenced code blocks as &lt;div class="mermaid"&gt;
    /// elements so that the mermaid.js client-side library can render them as diagrams.
    /// </summary>
    public static MarkdownPipelineBuilder UseMermaid(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<MermaidExtension>();
        return pipeline;
    }
}

/// <summary>
/// A Markdig extension that renders mermaid fenced code blocks as &lt;div class="mermaid"&gt;
/// elements so that the mermaid.js client-side library can render them as diagrams.
/// </summary>
public class MermaidExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // No builder-phase setup needed. The FencedCodeBlockExtension (added by
        // UseAdvancedExtensions) already parses fenced code blocks into AST nodes.
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is TextRendererBase<HtmlRenderer> htmlRenderer)
        {
            var original = htmlRenderer.ObjectRenderers.FindExact<CodeBlockRenderer>();
            if (original != null)
            {
                htmlRenderer.ObjectRenderers.Remove(original);
                htmlRenderer.ObjectRenderers.Add(new MermaidAwareCodeBlockRenderer());
            }
        }
    }
}

/// <summary>
/// Replaces the default <see cref="CodeBlockRenderer"/> to intercept mermaid-fenced
/// code blocks and render them as &lt;div class="mermaid"&gt; instead of &lt;pre&gt;&lt;code&gt;.
/// Non-mermaid code blocks are rendered identically to the original renderer.
/// </summary>
internal class MermaidAwareCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
{
    protected override void Write(HtmlRenderer renderer, CodeBlock obj)
    {
        if (obj is FencedCodeBlock { Info: "mermaid" })
        {
            renderer.EnsureLine();
            renderer.Write("<div class=\"mermaid\">");
            renderer.WriteLeafRawLines(obj, true, true);
            renderer.Write("</div>");
            renderer.EnsureLine();
            return;
        }

        // Default rendering for non-mermaid code blocks (identical to Markdig's built-in CodeBlockRenderer)
        renderer.EnsureLine();
        renderer.Write("<pre><code");
        if (obj is FencedCodeBlock fcb && !string.IsNullOrEmpty(fcb.Info))
        {
            renderer.Write(" class=\"language-");
            renderer.WriteEscape(fcb.Info);
            renderer.Write("\"");
        }
        renderer.Write(">");
        renderer.WriteLeafRawLines(obj, true, true);
        renderer.Write("</code></pre>");
        renderer.EnsureLine();
    }
}
