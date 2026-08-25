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
/// reads. API-key semantics are preserved: only the env-var NAME is stored; the key itself is
/// read at runtime and never displayed or persisted here.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _baseUrl = "";

    [ObservableProperty]
    private string _model = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiKeyStatus))]
    private string _apiKeyEnv = "";

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
        _baseUrl = config.BaseUrl;
        _model = config.Model;
        _apiKeyEnv = config.ApiKeyEnv;
        _defaultRevitVersion = config.DefaultRevitVersion;
        _defaultModelPath = config.DefaultModelPath;
        _themePreference = ThemeService.Preference;
    }

    public IReadOnlyList<AppThemePreference> ThemePreferences { get; } =
        [AppThemePreference.System, AppThemePreference.Light, AppThemePreference.Dark];

    partial void OnThemePreferenceChanged(AppThemePreference value) =>
        ThemeService.SetPreference(value, Application.Current.MainWindow);

    /// <summary>Live presence hint for the API key: probe the process, then the persisted
    /// User/Machine scopes. Never shows the value, only whether it resolves.</summary>
    public string ApiKeyStatus
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ApiKeyEnv))
                return "未填写环境变量名。";
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyEnv)))
                return "已在当前进程环境中检测到密钥。";
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyEnv, EnvironmentVariableTarget.User)))
                return "已在用户级环境变量中检测到密钥（重启应用后生效）。";
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyEnv, EnvironmentVariableTarget.Machine)))
                return "已在系统级环境变量中检测到密钥（重启应用后生效）。";
            return $"未检测到密钥。请设置环境变量后重试（如 setx {ApiKeyEnv} \"sk-...\"）。";
        }
    }

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
                ApiKeyEnv = string.IsNullOrWhiteSpace(ApiKeyEnv) ? "REVIT_AGENT_API_KEY" : ApiKeyEnv.Trim(),
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
