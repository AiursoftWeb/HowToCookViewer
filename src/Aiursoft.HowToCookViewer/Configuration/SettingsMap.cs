using Aiursoft.HowToCookViewer.Models;

namespace Aiursoft.HowToCookViewer.Configuration;

public class SettingsMap
{
    public const string ProjectName = "ProjectName";
    public const string BrandName = "BrandName";
    public const string BrandHomeUrl = "BrandHomeUrl";
    public const string ProjectLogo = "ProjectLogo";
    public const string AllowUserAdjustNickname = "Allow_User_Adjust_Nickname";
    public const string Icp = "Icp";
    public const string HowToCookRepoUrl = "HowToCookRepoUrl";
    public const string HowToCookRepoBackupUrl = "HowToCookRepoBackupUrl";
    public const string OpenAiInstance = "OpenAiInstance";
    public const string OpenAiLocalizationModel = "OpenAiLocalizationModel";
    public const string OpenAiApiToken = "OpenAiApiToken";
    public const string EmbeddingOllamaInstance = "EmbeddingOllamaInstance";
    public const string EmbeddingModel = "EmbeddingModel";
    public const string EmbeddingApiToken = "EmbeddingApiToken";
    public const string EnableEmbeddingBasedSearch = "EnableEmbeddingBasedSearch";
    public const string LocalizationLanguages = "LocalizationLanguages";
    public const string ShowVoxihostAd = "ShowVoxihostAd";
    public const string MaxCommentsPerDayPerUser = "MaxCommentsPerDayPerUser";

    public class FakeLocalizer
    {
        public string this[string name] => name;
    }

    private static readonly FakeLocalizer Localizer = new();

    public static readonly List<GlobalSettingDefinition> Definitions = new()
    {
        new GlobalSettingDefinition
        {
            Key = ProjectName,
            Name = Localizer["Project Name"],
            Description = Localizer["The name of the project displayed in the frontend."],
            Type = SettingType.Text,
            DefaultValue = "HowToCook"
        },
        new GlobalSettingDefinition
        {
            Key = BrandName,
            Name = Localizer["Brand Name"],
            Description = Localizer["The brand name displayed in the footer."],
            Type = SettingType.Text,
            DefaultValue = "Aiursoft"
        },
        new GlobalSettingDefinition
        {
            Key = BrandHomeUrl,
            Name = Localizer["Brand Home URL"],
            Description = Localizer[" The link to the brand's home page."],
            Type = SettingType.Text,
            DefaultValue = "https://www.aiursoft.com/"
        },
        new GlobalSettingDefinition
        {
            Key = ProjectLogo,
            Name = Localizer["Project Logo"],
            Description = Localizer["The logo of the project displayed in the navbar and footer. Support jpg, png, svg."],
            Type = SettingType.File,
            DefaultValue = "",
            Subfolder = "project-logo",
            AllowedExtensions = "jpg png svg",
            MaxSizeInMb = 5
        },
        new GlobalSettingDefinition
        {
            Key = AllowUserAdjustNickname,
            Name = Localizer["Allow User Adjust Nickname"],
            Description = Localizer["Allow users to adjust their nickname in the profile management page."],
            Type = SettingType.Bool,
            DefaultValue = "True"
        },
        new GlobalSettingDefinition
        {
            Key = Icp,
            Name = Localizer["ICP Number"],
            Description = Localizer["The ICP license number for China mainland users. Leave empty to hide."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = HowToCookRepoUrl,
            Name = Localizer["HowToCook Repo URL"],
            Description = Localizer["The Git repository URL to sync HowToCook recipes from."],
            Type = SettingType.Text,
            DefaultValue = "https://github.com/Anduin2017/HowToCook.git"
        },
        new GlobalSettingDefinition
        {
            Key = HowToCookRepoBackupUrl,
            Name = Localizer["HowToCook Repo Backup URL"],
            Description = Localizer["Fallback Git repository URL used when the primary URL times out."],
            Type = SettingType.Text,
            DefaultValue = "https://gitee.com/Anduin2017/HowToCook.git"
        },
        new GlobalSettingDefinition
        {
            Key = OpenAiInstance,
            Name = Localizer["OpenAI Chat Endpoint"],
            Description = Localizer["The OpenAI-compatible chat completions endpoint used for recipe translation, ingredient extraction, and other LLM tasks. E.g. https://ollama.example.com/v1/chat/completions. Unrelated to embedding/vector search."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = OpenAiLocalizationModel,
            Name = Localizer["Localization Model"],
            Description = Localizer["The LLM model name used for recipe translation and ingredient extraction, e.g. qwen3.5:27b-q8_0. Must be available at the OpenAI Chat Endpoint above. Unrelated to embedding/vector search."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = OpenAiApiToken,
            Name = Localizer["OpenAI API Token"],
            Description = Localizer["The bearer token for authenticating with the OpenAI Chat Endpoint, e.g. sk-abc123... or 5a0fbdefa19f.... Leave empty if the endpoint does not require authentication."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = EmbeddingOllamaInstance,
            Name = Localizer["Embedding Ollama Instance"],
            Description = Localizer["The Ollama API base URL used specifically for generating recipe and query embeddings (vector search). Only the host is used — /api/embed is appended automatically. Falls back to OpenAI Chat Endpoint when empty. E.g. https://ollama.example.com"],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = EmbeddingModel,
            Name = Localizer["Embedding Model"],
            Description = Localizer["The embedding model name, e.g. bge-m3:latest. Must be available at the Embedding Ollama Instance. Only used for vector search, not for translation."],
            Type = SettingType.Text,
            DefaultValue = "bge-m3:latest"
        },
        new GlobalSettingDefinition
        {
            Key = EmbeddingApiToken,
            Name = Localizer["Embedding API Token"],
            Description = Localizer["The bearer token for authenticating with the Embedding Ollama Instance, e.g. 5a0fbdefa19f.... Falls back to OpenAI API Token when empty."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = EnableEmbeddingBasedSearch,
            Name = Localizer["Enable Embedding-Based Search"],
            Description = Localizer["Master switch for semantic (vector-based) recipe search. When enabled and all dependencies are configured, search results display a green \"Search based on AI (Vector Database)\" badge and use cosine similarity ranking via the embedding model. When disabled, or when dependencies are missing, search silently falls back to keyword matching without the badge. Requires Embedding Ollama Instance (or OpenAI Chat Endpoint as fallback) and Embedding Model."],
            Type = SettingType.Bool,
            DefaultValue = "False"
        },
        new GlobalSettingDefinition
        {
            Key = LocalizationLanguages,
            Name = Localizer["Localization Languages"],
            Description = Localizer["Comma-separated BCP-47 language codes to translate recipes into, e.g. en-US,ja-JP,ko-KR,fr-FR"],
            Type = SettingType.Text,
            DefaultValue = "en-US,en-GB,zh-TW,zh-HK,ja-JP,ko-KR,vi-VN,th-TH,de-DE,fr-FR,es-ES,ru-RU,it-IT,pt-PT,pt-BR,ar-SA,nl-NL,sv-SE,pl-PL,tr-TR,ro-RO,da-DK,uk-UA,id-ID,fi-FI,hi-IN,el-GR"
        },
        new GlobalSettingDefinition
        {
            Key = ShowVoxihostAd,
            Name = Localizer["Show Voxihost Ad"],
            Description = Localizer["Display a promotion banner on the home page thanking Voxihost for sponsoring the server."],
            Type = SettingType.Bool,
            DefaultValue = "False"
        },
        new GlobalSettingDefinition
        {
            Key = MaxCommentsPerDayPerUser,
            Name = Localizer["Max Comments Per Day Per User"],
            Description = Localizer["The maximum number of comments a user can post per day."],
            Type = SettingType.Number,
            DefaultValue = "10"
        }
    };
}
