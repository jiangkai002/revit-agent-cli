using RevitAgent.Cli;
using RevitAgent.Cli.Commands;

// Do NOT touch the console codepage here — neither InputEncoding nor OutputEncoding.
// SetConsoleCP / SetConsoleOutputCP mutate the SHARED console (conhost) and are NOT
// restored when this process exits, so the caller's terminal is left polluted:
//   - InputEncoding  = UTF-8 (65001) breaks Win10 Chinese IME composition + paste;
//   - OutputEncoding = UTF-8 (65001) leaves the output CP at 65001, after which PSReadLine
//     encodes typed IME text per its cached 936 → GBK bytes echoed through a 65001 console
//     => every later TYPED Chinese in that terminal is mojibake ("??????"), even though
//     paste/parsing still work. (This is the symptom that forced removing OutputEncoding.)
// The system default (936/GBK on zh-CN Windows) already renders Chinese correctly, and the
// markers the process display uses (▎ → ← …) are all GBK-representable, so the CLI's own
// output (answer + gray reasoning/tool lines) needs no codepage change at all. (Non-GBK
// chars like emoji in an answer may show as '?' — acceptable for Chinese answers.) The
// executor's result file is UTF-8 via File.WriteAllText (independent of the console CP),
// and its drained+discarded stdout echo inherits the console's CP, so nothing breaks.
// Enable Windows Virtual Terminal so the gray "process" display renders as ANSI dim/gray
// instead of littering the output with escape bytes. No-op when redirected or non-Windows.
ConsoleAnsi.EnsureEnabled();

// `revit-agent` (no args) or `revit-agent --model-path X --version 2022 ...` (option flags, no
// verb) both drop straight into the interactive REPL (claude-style): a leading option flag is
// not a subcommand. The help flags are intercepted first so `revit-agent -h` still prints help.
// `revit-agent chat` is an explicit alias that takes the same options.
var first = args.Length > 0 ? args[0] : null;
var isHelp = first is "-h" or "--help" or "help";
if (first is null || (!isHelp && first.StartsWith('-')))
    return await ChatCommand.RunAsync(args);

var verb = first!;
var rest = args[1..];

return verb switch
{
    "config" => ConfigCommand.Run(rest),
    "run" => await RunCommand.RunAsync(rest),
    "chat" => await ChatCommand.RunAsync(rest),
    "exec" => await ExecCommand.RunAsync(rest),
    "skill" => await SkillCommand.RunAsync(rest),
    "help" or "-h" or "--help" => PrintHelp(),
    _ => PrintHelp($"未知命令: {verb}")
};

static int PrintHelp(string? note = null){
    if (note is not null) Console.WriteLine(note);
    Console.WriteLine("revit-agent — 无头 Revit 智能体 CLI");
    Console.WriteLine();
    Console.WriteLine("命令:");
    Console.WriteLine("  run \"<需求>\"   自然语言描述需求，智能体生成并执行 Revit 代码后返回结果");
    Console.WriteLine("  chat          进入交互会话（多轮）");
    Console.WriteLine("  exec <cs>     直接运行一段 Revit 二次开发代码（不经 LLM，用于测试）");
    Console.WriteLine("  config        查看或编辑配置 (init/set/get/path)");
    Console.WriteLine("  skill         安装/查看/移除技能 (install <url>|list|show <name>|remove <name>|path)");
    Console.WriteLine();
    Console.WriteLine("通用选项:");
    Console.WriteLine("  --version <2021|2022>  Revit 版本（默认取配置）");
    Console.WriteLine("  --model-path <文件或目录>  Revit 模型路径（目录则扫描其中所有 .rvt；省略则扫描当前目录）");
    Console.WriteLine("  --model <name>         LLM 模型名（默认取配置）");
    return 1;
}
