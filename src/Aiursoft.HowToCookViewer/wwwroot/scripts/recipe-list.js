document.addEventListener('DOMContentLoaded', () => {
    const sentinel = document.getElementById('load-sentinel');
    if (!sentinel) return;

    const grid = document.getElementById('recipe-grid');
    const spinner = document.getElementById('load-spinner');
    const error = document.getElementById('load-error');
    let nextPage = 2;
    let loading = false;
    let hasMore = true;
    let failed = false;

    function buildUrl(page) {
        const params = new URLSearchParams({ page });
        if (sentinel.dataset.category) params.set('category', sentinel.dataset.category);
        if (sentinel.dataset.difficulty) params.set('difficulty', sentinel.dataset.difficulty);
        if (sentinel.dataset.sortBy) params.set('sortBy', sentinel.dataset.sortBy);
        return '/Recipes/LoadMore?' + params.toString();
    }

    async function loadMore() {
        if (loading || !hasMore || failed) return;
        loading = true;
        spinner.style.display = '';
        error.classList.add('d-none');
        try {
            const response = await fetch(buildUrl(nextPage));
            if (!response.ok) throw new Error('Recipe request failed');
            const html = await response.text();
            grid.insertAdjacentHTML('beforeend', html);
            hasMore = response.headers.get('X-Has-More') === 'true';
            nextPage++;
            if (typeof lucide !== 'undefined') lucide.createIcons();
        } catch {
            failed = true;
            error.classList.remove('d-none');
        } finally {
            loading = false;
            spinner.style.display = 'none';
            if (!hasMore) {
                sentinel.style.display = 'none';
                observer.disconnect();
            }
        }
    }

    const observer = new IntersectionObserver(entries => {
        if (entries[0].isIntersecting) loadMore();
    }, { rootMargin: '200px' });
    observer.observe(sentinel);
    document.getElementById('retry-load').addEventListener('click', () => {
        failed = false;
        loadMore();
    });
});
