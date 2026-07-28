(function () {
    'use strict';
    const renderedMermaidAttribute = 'data-aiursoft-mermaid-rendered';
    function getContainers(selector) { return Array.from(document.querySelectorAll(selector || '.markdown-content')); }
    function highlight(containers) {
        if (!window.hljs) return;
        containers.forEach(container => container.querySelectorAll('pre code:not(.language-mermaid)').forEach(block => {
            if (!block.dataset.highlighted) window.hljs.highlightElement(block);
        }));
    }
    async function renderMermaid(containers, theme) {
        if (!window.mermaid) return;
        window.mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: theme || 'default' });
        const blocks = containers.flatMap(container => Array.from(container.querySelectorAll('pre code.language-mermaid, pre.mermaid, .mermaid')));
        await Promise.all(blocks.map(async (block, index) => {
            const target = block.matches('code') ? block.closest('pre') : block;
            if (!target || target.hasAttribute(renderedMermaidAttribute)) return;
            target.setAttribute(renderedMermaidAttribute, 'true');
            try {
                const result = await window.mermaid.render(`aiursoft-mermaid-${index}-${crypto.randomUUID()}`, block.textContent || '');
                const wrapper = document.createElement('div');
                wrapper.className = 'mermaid-rendered';
                wrapper.innerHTML = result.svg;
                target.replaceWith(wrapper);
                if (result.bindFunctions) result.bindFunctions(wrapper);
            } catch (error) {
                target.removeAttribute(renderedMermaidAttribute);
                console.error('Mermaid render failed.', error);
            }
        }));
    }
    async function renderMath(containers) {
        if (window.MathJax?.startup?.promise) await window.MathJax.startup.promise;
        if (window.MathJax?.typesetPromise) await window.MathJax.typesetPromise(containers);
    }
    async function render(options) {
        const settings = options || {};
        const containers = getContainers(settings.selector);
        if (!containers.length) return;
        highlight(containers);
        await renderMermaid(containers, settings.theme);
        await renderMath(containers);
    }
    async function print(options) {
        try { await render(options); } catch (error) { console.error('Markdown preparation for printing failed.', error); }
        window.print();
    }
    window.AiursoftMarkdown = { render, print };
})();
