using Aiursoft.HowToCookViewer.Tests.IntegrationTests;
using Microsoft.Playwright;

namespace Aiursoft.HowToCookViewer.Tests.BrowserTests;

[TestCategory("Browser")]
public abstract class BrowserTestBase : TestBase
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private bool _tracing;

    public TestContext TestContext { get; set; } = null!;
    protected IPage Page { get; private set; } = null!;
    protected virtual IBrowserType BrowserType(IPlaywright playwright) => playwright.Chromium;
    protected virtual BrowserTypeLaunchOptions LaunchOptions => new()
    {
        Channel = "chrome",
        Headless = true,
        ExecutablePath = Environment.GetEnvironmentVariable("TEMPLATE_TEST_BROWSER_EXECUTABLE")
    };

    [TestInitialize]
    public async Task StartBrowserAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await BrowserType(_playwright).LaunchAsync(LaunchOptions);
        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Http.BaseAddress!.AbsoluteUri
        });
        await _context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true });
        _tracing = true;
        Page = await _context.NewPageAsync();
    }

    [TestCleanup]
    public async Task StopBrowserAsync()
    {
        try
        {
            if (_context != null)
            {
                try
                {
                    await SaveDiagnosticsAsync(_context);
                }
                finally
                {
                    await _context.CloseAsync();
                }
            }
        }
        finally
        {
            try
            {
                if (_browser != null)
                {
                    await _browser.CloseAsync();
                }
            }
            finally
            {
                _playwright?.Dispose();
            }
        }
    }

    private async Task SaveDiagnosticsAsync(IBrowserContext context)
    {
        if (TestContext.CurrentTestOutcome == UnitTestOutcome.Passed)
        {
            if (_tracing) await context.Tracing.StopAsync();
            return;
        }

        var directory = Directory.CreateDirectory(Path.Combine(
            Environment.GetEnvironmentVariable("TEMPLATE_TEST_RESULTS_DIR") ?? TestContext.ResultsDirectory!,
            "browser", Guid.NewGuid().ToString("N"))).FullName;
        if (_tracing)
        {
            await CaptureAsync("trace.zip", path => context.Tracing.StopAsync(new() { Path = path }));
        }
        if (Page is { IsClosed: false })
        {
            await CaptureAsync("page.png", path => Page.ScreenshotAsync(new() { Path = path, Timeout = 5_000 }));
        }

        async Task CaptureAsync(string name, Func<string, Task> capture)
        {
            var path = Path.Combine(directory, name);
            try
            {
                await capture(path);
                TestContext.AddResultFile(path);
            }
            catch (PlaywrightException exception)
            {
                TestContext.WriteLine($"Failed to save {name}: {exception.Message}");
            }
        }
    }
}
