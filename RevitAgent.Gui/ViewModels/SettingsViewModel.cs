using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RevitAgent.Cli;
using RevitAgent.Gui.Services;
using System.Windows;

namespace RevitAgent.Gui.ViewModels;

/// <summary>
/// Edits a clone of <see cref="AgentConfig"/> (loaded fresh when the app starts) and saves it
/// via <see cref="ConfigStore.Save"/> to the same %APPDATA%\revit-agent\config.json the CLI
/// reads. The API key is stored in that file (editable here); the legacy env var named by
/// <see cref="AgentConfig.ApiKeyEnv"/> still works as a fallback when the field is empty.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly string _loadedApiKeyEnv; // preserved on save; env-var fallback name (legacy)

    [ObservableProperty]
    private string _baseUrl = "";

    [ObservableProperty]
    private string _model = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiKeyStatus))]
    private string _apiKey = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiKeyStatus))]
    private int _defaultRevitVersion;

    [ObservableProperty]
    private string _defaultModelPath = "";

    [ObservableProperty]
    private string _saveMessage = "";

    [ObservableProperty]
    private bool _saveMessageIsError;

    [ObservableProperty]
    private AppThemePreference _themePreference;

    public IReadOnlyList<int> RevitVersions { get; } = [2019, 2020, 2021, 2022];

    public SettingsViewModel()
    {
        var config = ConfigStore.Load();
        _loadedApiKeyEnv = config.ApiKeyEnv;
        _baseUrl = config.BaseUrl;
        _model = config.Model;
        _apiKey = config.ApiKey;
        _defaultRevitVersion = config.DefaultRevitVersion;
        _defaultModelPath = config.DefaultModelPath;
        _themePreference = ThemeService.Preference;
    }

    public IReadOnlyList<AppThemePreference> ThemePreferences { get; } =
        [AppThemePreference.System, AppThemePreference.Light, AppThemePreference.Dark];

    partial void OnThemePreferenceChanged(AppThemePreference value) =>
        ThemeService.SetPreference(value, Application.Current.MainWindow);

    /// <summary>Live presence hint for the API key. Never shows the value itself.</summary>
    public string ApiKeyStatus =>
        string.IsNullOrWhiteSpace(ApiKey)
            ? "未填写密钥。发起对话将无法调用大模型（留空时会回退读取环境变量）。"
            : "已配置密钥，新会话即可使用。";

    [RelayCommand]
    private void Save()
    {
        try
        {
            ConfigStore.Save(new AgentConfig
            {
                Provider = "openai",
                BaseUrl = BaseUrl.Trim(),
                Model = Model.Trim(),
                ApiKey = ApiKey.Trim(),
                ApiKeyEnv = _loadedApiKeyEnv,
                DefaultRevitVersion = DefaultRevitVersion,
                DefaultModelPath = DefaultModelPath.Trim(),
            });
            SaveMessage = "已保存。运行中的会话不会自动应用，请新建会话后生效。";
            SaveMessageIsError = false;
        }
        catch (Exception ex)
        {
            SaveMessage = "保存失败: " + ex.Message;
            SaveMessageIsError = true;
        }
    }

    [RelayCommand]
    private void BrowseModelPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Revit 模型文件",
            // OFD cannot pick a directory; the .rvt file itself becomes the default batch
            // (a single model). The text box stays editable for direct directory paths —
            // a directory is scanned for top-level *.rvt at conversation start.
            Filter = "Revit 模型 (*.rvt)|*.rvt",
        };
        if (dialog.ShowDialog() != true) return;
        DefaultModelPath = dialog.FileName;
    }
}
