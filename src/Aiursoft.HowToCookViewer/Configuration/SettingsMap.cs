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
    public const string OllamaInstance = "OllamaInstance";
    public const string OllamaModel = "OllamaModel";
    public const string OllamaToken = "OllamaToken";
    public const string LocalizationLanguages = "LocalizationLanguages";

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
            Key = OllamaInstance,
            Name = Localizer["Ollama API Endpoint"],
            Description = Localizer["The OpenAI-compatible chat completions endpoint for recipe translation, e.g. https://ollama.example.com/api/chat/completions"],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = OllamaModel,
            Name = Localizer["Ollama Model"],
            Description = Localizer["The AI model name to use for recipe translation, e.g. qwen3.5:27b-q8_0"],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = OllamaToken,
            Name = Localizer["Ollama API Token"],
            Description = Localizer["The bearer token for authenticating with the Ollama/OpenAI endpoint."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = LocalizationLanguages,
            Name = Localizer["Localization Languages"],
            Description = Localizer["Comma-separated BCP-47 language codes to translate recipes into, e.g. en-US,ja-JP,ko-KR,fr-FR"],
            Type = SettingType.Text,
            DefaultValue = "en-US,en-GB,zh-TW,zh-HK,ja-JP,ko-KR,vi-VN,th-TH,de-DE,fr-FR,es-ES,ru-RU,it-IT,pt-PT,pt-BR,ar-SA,nl-NL,sv-SE,pl-PL,tr-TR,ro-RO,da-DK,uk-UA,id-ID,fi-FI,hi-IN,el-GR"
        }
    };
}
