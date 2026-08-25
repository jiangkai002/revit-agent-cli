using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using RevitAgent.Cli;
using RevitAgent.Gui.Models;

namespace RevitAgent.Gui.ViewModels;

/// <summary>
/// Skills management: lists bundled + user skills, installs from a URL or local ZIP, and removes
/// user skills. After any change, broadcasts <see cref="SkillsChangedMessage"/>
/// — the agent's skills catalog is frozen at first agent build, so the chat page tells the
/// user a new conversation is needed for new skills to take effect.
/// </summary>
public partial class SkillsViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private string _installUrl = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _statusIsError;

    public ObservableCollection<SkillManifest> Skills { get; } = [];

    public string UserSkillsDirectory => SkillStore.SkillsDirectory;

    public string BundledSkillsInfo =>
        SkillStore.BundledSkillsDirectory is { } bundled ? bundled : "（未找到捆绑技能目录）";

    public SkillsViewModel()
    {
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Skills.Clear();
        foreach (var skill in SkillStore.ListInstalled())
            Skills.Add(skill);
    }

    private bool CanInstall() => !IsInstalling && !string.IsNullOrWhiteSpace(InstallUrl);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        var url = InstallUrl.Trim();
        if (url.Length == 0) return;
        await RunInstallAsync(() => SkillStore.InstallFromUrlAsync(url), clearUrl: true);
    }

    public async Task InstallLocalZipAsync(string zipPath)
    {
        if (IsInstalling) return;
        await RunInstallAsync(() => SkillStore.InstallFromZipAsync(zipPath), clearUrl: false);
    }

    private async Task RunInstallAsync(
        Func<Task<(bool Ok, string Message)>> install,
        bool clearUrl)
    {
        IsInstalling = true;
        try
        {
            var (ok, message) = await install();
            StatusIsError = !ok;
            StatusMessage = message + (ok ? "（新技能需新建会话后才会进入智能体目录。）" : "");
            if (ok)
            {
                if (clearUrl) InstallUrl = "";
                Refresh();
                WeakReferenceMessenger.Default.Send(new SkillsChangedMessage());
            }
        }
        finally
        {
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private void RemoveSkill(SkillManifest skill)
    {
        // Bundled skills cannot be removed — SkillStore refuses; surface its message.
        var (ok, message) = SkillStore.Remove(skill.Name);
        StatusIsError = !ok;
        StatusMessage = message;
        if (ok)
        {
            Refresh();
            WeakReferenceMessenger.Default.Send(new SkillsChangedMessage());
        }
    }
}
