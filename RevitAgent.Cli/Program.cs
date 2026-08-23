using System.Text;
using RevitAgent.Cli;
using RevitAgent.Cli.Commands;

// The executor mutates the shared console codepage to UTF-8; set it here so the
// CLI's own output (and any leftover executor state) renders Chinese correctly.
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
// Enable Windows Virtual Terminal so the gray "process" display (reasoning, tool
// calls) renders as ANSI dim/gray instead of littering the output with escape bytes.
// No-op when output is redirected or on non-Windows.
ConsoleAnsi.EnsureEnabled();

if (args.Length == 0)
{
    PrintHelp();
    return 1;
}

var verb = args[0];
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
