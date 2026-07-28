using Aiursoft.CSTools.Tools;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Canon.ScheduledTasks;
using Aiursoft.DbTools.Switchable;
using Aiursoft.Dotlang.Shared;
using Aiursoft.GitRunner;
using Aiursoft.GptClient.Services;
using Aiursoft.Scanner;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.WebTools.Abstractions.Models;
using Aiursoft.HowToCookViewer.InMemory;
using Aiursoft.HowToCookViewer.MySql;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.Authentication;
using Aiursoft.HowToCookViewer.Services.BackgroundJobs;
using Aiursoft.HowToCookViewer.Sqlite;
using Aiursoft.UiStack;
using Aiursoft.UiStack.Layout;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc.Razor;
using Aiursoft.ClickhouseLoggerProvider;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using Ganss.Xss;
using Markdig;

namespace Aiursoft.HowToCookViewer;

[ExcludeFromCodeCoverage]
public class Startup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        // AppSettings.
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));

        // Relational database
        var (connectionString, dbType, allowCache) = configuration.GetDbSettings();
        services.AddSwitchableRelationalDatabase(
            dbType: EntryExtends.IsInUnitTests() ? "InMemory" : dbType,
            connectionString: connectionString,
            supportedDbs:
            [
                new MySqlSupportedDb(allowCache: allowCache, splitQuery: false),
                new SqliteSupportedDb(allowCache: allowCache, splitQuery: true),
                new InMemorySupportedDb()
            ]);

        services.AddLogging(builder =>
        {
            builder.AddClickhouse(options => configuration.GetSection("Logging:Clickhouse").Bind(options));
        });

        // Authentication and Authorization
        services.AddTemplateAuth(configuration);

        // Services
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddAssemblyDependencies(typeof(Startup).Assembly);
        services.AddSingleton<NavigationState<Startup>>();
        services.AddHttpContextAccessor();
        services.AddScoped<RecipeLocalizationService>();
        services.AddScoped<ChatClient>();
        services.AddScoped<MarkdownShredder>();
        services.AddSingleton<RecipeEmbeddingCache>();
        services.AddSingleton<IngredientGroupService>();
        services.AddSingleton<SearchRateLimiter>();
        services.AddScoped<RecipeVectorSearchService>();
        services.AddScoped<IRecipeTranslationService, RecipeTranslationService>();
        services.AddGitRunner();

        // Background job infrastructure
        services.AddTaskQueueEngine();
        services.AddScheduledTaskEngine();

        // Background jobs
        services.RegisterBackgroundJob<DummyJob>();
        var orphanAvatarCleanupJob = services.RegisterBackgroundJob<OrphanAvatarCleanupJob>();
        var syncHowToCookRepoJob = services.RegisterBackgroundJob<SyncHowToCookRepoJob>();
        var indexRecipesJob = services.RegisterBackgroundJob<IndexRecipesJob>();
        var localizeRecipesJob = services.RegisterBackgroundJob<LocalizeRecipesJob>();
        var indexTipsJob = services.RegisterBackgroundJob<IndexTipsJob>();
        var localizeTipsJob = services.RegisterBackgroundJob<LocalizeTipsJob>();
        var extractIngredientsJob = services.RegisterBackgroundJob<ExtractIngredientsJob>();
        var generateEmbeddingsJob = services.RegisterBackgroundJob<GenerateEmbeddingsJob>();
        var refreshEmbeddingCacheJob = services.RegisterBackgroundJob<RefreshEmbeddingCacheJob>();
        var cleanupLocalizedRecipesJob = services.RegisterBackgroundJob<CleanupLocalizedRecipesJob>();
        services.RegisterBackgroundJob<ResetRecipeDataJob>(); // manual-only, no schedule

        // Scheduled tasks (attach a schedule to any registered background job)
        services.RegisterScheduledTask(
            registration: orphanAvatarCleanupJob,
            period: TimeSpan.FromHours(6),
            startDelay: TimeSpan.FromMinutes(5));

        services.RegisterScheduledTask(
            registration: syncHowToCookRepoJob,
            period: TimeSpan.FromHours(4),
            startDelay: TimeSpan.FromMinutes(1));

        services.RegisterScheduledTask(
            registration: indexRecipesJob,
            period: TimeSpan.FromHours(4),
            startDelay: TimeSpan.FromMinutes(20));

        // Localize recipes after indexing (start after 30 min, then every 30 min)
        services.RegisterScheduledTask(
            registration: localizeRecipesJob,
            period: TimeSpan.FromMinutes(30),
            startDelay: TimeSpan.FromMinutes(30));

        services.RegisterScheduledTask(
            registration: indexTipsJob,
            period: TimeSpan.FromHours(4),
            startDelay: TimeSpan.FromMinutes(22));

        services.RegisterScheduledTask(
            registration: localizeTipsJob,
            period: TimeSpan.FromMinutes(30),
            startDelay: TimeSpan.FromMinutes(35));

        services.RegisterScheduledTask(
            registration: extractIngredientsJob,
            period: TimeSpan.FromMinutes(30),
            startDelay: TimeSpan.FromMinutes(40));

        // Generate embeddings every 30 min (starts after 50 min, after ingredients extraction)
        services.RegisterScheduledTask(
            registration: generateEmbeddingsJob,
            period: TimeSpan.FromMinutes(30),
            startDelay: TimeSpan.FromMinutes(50));

        // Refresh embedding cache every 8 hours (starts after 1 min)
        services.RegisterScheduledTask(
            registration: refreshEmbeddingCacheJob,
            period: TimeSpan.FromHours(8),
            startDelay: TimeSpan.FromMinutes(1));

        // Cleanup stale LocalizedRecipes every 6 hours (starts after 55 min)
        services.RegisterScheduledTask(
            registration: cleanupLocalizedRecipesJob,
            period: TimeSpan.FromHours(6),
            startDelay: TimeSpan.FromMinutes(55));

        // Add the markdown pipeline and HTML sanitizer
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseMermaid().DisableHtml().Build();
        services.AddSingleton(pipeline);
        services.AddSingleton(_ =>
        {
            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedTags.Add("br");
            sanitizer.AllowedAttributes.Add("class");
            return sanitizer;
        });

        // Controllers and localization
        services.AddControllersWithViews()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                options.SerializerSettings.ContractResolver = new DefaultContractResolver();
            })
            .AddApplicationPart(typeof(Startup).Assembly)
            .AddApplicationPart(typeof(UiStackLayoutViewModel).Assembly)
            .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
            .AddDataAnnotationsLocalization();
    }

    public void Configure(WebApplication app)
    {
        app.UseExceptionHandler("/Error/Code500");
        app.UseStatusCodePagesWithReExecute("/Error/Code{0}");
        app.UseUIStack();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDefaultControllerRoute();
    }
}
