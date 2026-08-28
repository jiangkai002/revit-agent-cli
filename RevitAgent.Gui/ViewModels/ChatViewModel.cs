using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using RevitAgent.Cli;
using RevitAgent.Gui.Models;
using RevitAgent.Gui.Services;

namespace RevitAgent.Gui.ViewModels;

/// <summary>
/// The chat page: owns one conversation (one <see cref="AgentHost"/> = one AgentSession =
/// multi-turn memory; 新会话 drops the host), the current model batch (the /rvt equivalent,
/// switched via the file picker) and the Revit version (per-conversation). Turns are strictly
/// serialized: Send is disabled while busy; 停止 cancels the turn's token, which kills the
/// in-flight executor process tree. Agent process events arrive through
/// <see cref="GuiTurnDisplayFactory"/> as <see cref="TurnEvent"/>s on the dispatcher.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private AgentHost? _host;
    private CancellationTokenSource? _turnCts;
    private List<string> _modelPaths = [];
    private ReasoningSection? _openReasoning;
    private readonly GuiTurnDisplayFactory _displayFactory;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewConversationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StageDisplayText))]
    private string _stageText = "";

    public string StageDisplayText => string.IsNullOrWhiteSpace(StageText) ? "就绪" : StageText;

    [ObservableProperty]
    private string _modelSummary = "未选择模型";

    [ObservableProperty]
    private int _revitVersion;

    public IReadOnlyList<int> RevitVersions { get; } = [2019, 2020, 2021, 2022];

    public ObservableCollection<ChatItem> Items { get; } = [];

    public ChatViewModel()
    {
        var config = ConfigStore.Load();
        RevitVersion = config.DefaultRevitVersion;
        _displayFactory = new GuiTurnDisplayFactory(HandleTurnEvent);
        ResolveInitialModels(config);

        // Hidden demo hook: set REVITAGENT_DEMO=1 when launching to pre-seed the transcript with
        // representative content (reasoning, a tool call with full C# source, a tool result, a
        // markdown answer). No agent/dialog/API involved — purely for screenshots and demos.
        if (Environment.GetEnvironmentVariable("REVITAGENT_DEMO") == "1")
            SeedDemoTranscript();

        WeakReferenceMessenger.Default.Register<SkillsChangedMessage>(this, (_, _) =>
            Items.Add(new InfoItem("技能列表已变化，新建会话后新技能才会进入智能体目录。")));
    }

    private void SeedDemoTranscript()
    {
        Items.Add(new UserMessage("请帮我检查 1F 的火警系统，用设备摄像机模型替代不合理的设备点位，并统计数量。"));
        _openReasoning = new ReasoningSection
        {
            Text = "用户要求对 1F 火警点位做合规检查。先枚举当前模型中的火警设备与房间数据，再用摄像机族模板生成替代点位，最后汇总数量与缺失房间。整个流程需要一次 Revit 会话完成。"
        };
        Items.Add(_openReasoning);
        Items.Add(new ToolCallItem("RunRevitCode", """
var fec = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance));
var fire = fec.Where(e => e.Name.Contains("火灾显示盘") || e.Name.Contains("烟感")).ToList();
var rooms = new FilteredElementCollector(doc).OfClass(typeof(SpatialElement)).Cast<SpatialElement>();
var missing = rooms.Where(r => !fire.Any(fi => r.BoundingBox?.Intersects(fi.BoundingBox) == true)).ToList();
return new { 已安装 = fire.Count, 房间数 = rooms.Count(), 缺点位房间 = missing.Count };
""".Trim()));
        Items.Add(new ToolResultItem("""{"Ok":true,"Result":{"已安装":132,"房间数":86,"缺点位房间":3}}""".Trim()));
        Items.Add(new AssistantMessage(
            """
## 检查结论

1F 火警点位检查完成，**3 个房间缺点位**，其余 83 个房间均已覆盖。

### 缺失明细
| 房间 | 楼层 | 类型 |
|------|------|------|
| 更衣室 | 1F | 卫浴 |
| 设备机房 | 1F | 机房 |

### 修复建议
```csharp
var cam = new FilteredElementCollector(doc)
    .OfClass(typeof(FamilyInstance))
    .FirstOrDefault(e => e.Symbol.FamilyName.Contains("摄像机"));
```
按此模板自上而下补点。需要我继续执行吗？
""".Trim()));
    }

    // The Revit version is baked into the executor session, so changing it mid-conversation
    // would run code against a different Revit — drop the host so the next message starts a
    // fresh session (memory resets; the user is told).
    partial void OnRevitVersionChanged(int value)
    {
        if (_host is null) return;
        _host = null;
        _openReasoning = null;
        Items.Add(new InfoItem($"已切换 Revit 版本为 {value}，下一条消息将开启新会话。"));
    }

    private void ResolveInitialModels(AgentConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.DefaultModelPath))
        {
            try
            {
                var paths = ResolveModelPaths(config.DefaultModelPath);
                if (paths.Count > 0) _modelPaths = paths;
            }
            catch
            {
                // Bad path in config: fall through to the picker on first send.
            }
        }
        UpdateModelSummary();
    }

    /// <summary>File → single entry; directory → top-level *.rvt sorted; absolute paths (the
    /// executor subprocess runs with its own working directory). GUI-local replacement for
    /// OptionParser.ResolveModelPaths, which prints to Console.Error.</summary>
    private static List<string> ResolveModelPaths(string path)
    {
        path = Path.GetFullPath(path);
        if (File.Exists(path)) return [path];
        if (Directory.Exists(path))
            return Directory.EnumerateFiles(path, "*.rvt").Order(StringComparer.OrdinalIgnoreCase).ToList();
        return [];
    }

    private void UpdateModelSummary() =>
        ModelSummary = _modelPaths.Count > 0 ? $"当前 {_modelPaths.Count} 个模型" : "未选择模型";

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (text.Length == 0) return;
        InputText = "";

        // GUIs have no meaningful CWD to scan (the CLI fallback); with no configured batch,
        // the picker is the only source of models.
        if (_modelPaths.Count == 0 && !PromptForModels())
            return; // picker cancelled — drop the message rather than fail the turn

        Items.Add(new UserMessage(text));
        await RunTurnAsync(text);
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop() => _turnCts?.Cancel();

    [RelayCommand]
    private void PickModels()
    {
        if (PromptForModels() && _host is not null)
            _host.SetModelPaths(_modelPaths); // live batch switch between turns, like /rvt
    }

    private bool PromptForModels()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Revit 模型（可多选）",
            Filter = "Revit 模型 (*.rvt)|*.rvt",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return false;
        var paths = dialog.FileNames
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0) return false;
        _modelPaths = paths;
        UpdateModelSummary();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanStartNew))]
    private void NewConversation()
    {
        _host = null; // a new conversation = a new AgentHost (fresh AgentSession memory)
        _openReasoning = null;
        Items.Clear();
        StageText = "";
    }

    private static bool CanStartNew() => true;

    private async Task RunTurnAsync(string request)
    {
        // The host reads config at construction (API key, endpoint, model) and caches the
        // OpenAIClient, so it is created lazily here — settings changes apply from the next
        // conversation on, exactly like a CLI restart.
        if (_host is null)
            _host = new AgentHost(ConfigStore.Load(), modelOverride: null, _modelPaths, RevitVersion, _displayFactory);

        IsBusy = true;
        StageText = "分析需求中";
        _turnCts = new CancellationTokenSource();
        var ct = _turnCts.Token;
        try
        {
            var answer = await _host.AskAsync(request, ct);
            Items.Add(new AssistantMessage(answer));
        }
        catch (OperationCanceledException)
        {
            Items.Add(new InfoItem("已取消"));
        }
        catch (InvalidOperationException ex) // e.g. missing API key
        {
            Items.Add(new ErrorMessage(ex.Message));
            Items.Add(new InfoItem("请到「设置」页填写 API 密钥后重试。"));
        }
        catch (Exception ex)
        {
            Items.Add(new ErrorMessage("错误: " + ex.Message));
        }
        finally
        {
            _turnCts.Dispose();
            _turnCts = null;
            IsBusy = false;
            StageText = "";
            _openReasoning = null;
        }
    }

    /// <summary>Dispatcher-thread sink for the agent's process events (see GuiTurnDisplayFactory).</summary>
    private void HandleTurnEvent(TurnEvent e)
    {
        switch (e)
        {
            case StageEvent stage:
                StageText = stage.Stage;
                break;
            case ExecutingEvent:
                StageText = "执行中";
                break;
            case ReasoningDeltaEvent delta:
                if (_openReasoning is null)
                {
                    _openReasoning = new ReasoningSection();
                    Items.Add(_openReasoning);
                }
                _openReasoning.Text += delta.Chunk;
                break;
            case PreambleEvent preamble:
                CloseReasoningSection();
                Items.Add(new InfoItem(preamble.Text));
                break;
            case ToolCallEvent call:
                CloseReasoningSection();
                Items.Add(new ToolCallItem(call.Name, call.ArgumentsText));
                break;
            case ToolResultEvent result:
                CloseReasoningSection();
                Items.Add(new ToolResultItem(result.ResultText));
                break;
            case TurnCompletedEvent:
                CloseReasoningSection();
                break;
        }
    }

    private void CloseReasoningSection() => _openReasoning = null;
}
