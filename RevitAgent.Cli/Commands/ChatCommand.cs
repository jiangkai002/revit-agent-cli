namespace RevitAgent.Cli.Commands;

public static class ChatCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var opts = OptionParser.Parse(args);
        var config = ConfigStore.Load();
        var modelPaths = OptionParser.ResolveModelPaths(opts, config);
        var version = OptionParser.ResolveVersion(opts, config);

        if (modelPaths.Count == 0)
        {
            Console.Error.WriteLine("未找到 Revit 模型文件。请通过 --model-path 指定文件或目录，或在含 .rvt 的目录下运行。用 `revit-agent help` 查看完整用法。");
            return 1;
        }

        var host = new AgentHost(config, opts.Model, modelPaths, version);
        Console.WriteLine($"RevitAgent 交互会话已启动（{modelPaths.Count} 个模型，Revit {version}）。输入 /rvt 选择模型（交互）；Ctrl+C 退出（或输入 exit）。");

        using var cts = new CancellationTokenSource();
        var state = new ReplState();
        // Ctrl+C: idle at the prompt -> leave e.Cancel=false so the default SIGINT terminates
        // instantly (no executor/temp is running, so nothing to clean up). Mid-run -> intercept
        // (e.Cancel=true) so we can cancel the agent loop + kill the executor and exit cleanly
        // instead of hard-killing (which would leak temp files + orphan Revit).
        Console.CancelKeyPress += (_, e) =>
        {
            if (!state.Busy) return;        // idle: default termination
            e.Cancel = true;                 // mid-run: intercept, clean up, then break out
            state.ExitRequested = true;
            cts.Cancel();
        };

        try
        {
            while (true)
            {
                if (state.ExitRequested) break;
                Console.Write("> ");
                var line = Console.ReadLine();
                if (line is null) break;     // EOF (Ctrl+Z / stdin closed)
                if (line.Trim() is "exit" or "quit") break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                // /rvt is a local REPL command (no LLM): inspect/switch the effective model batch.
                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 0 && tokens[0].Equals("/rvt", StringComparison.OrdinalIgnoreCase))
                {
                    HandleRvtCommand(tokens[1..], host);
                    continue;
                }

                state.Busy = true;
                try
                {
                    var answer = await host.AskAsync(line, cts.Token);
                    if (state.ExitRequested) break;
                    Console.WriteLine(answer);
                }
                catch (OperationCanceledException) when (state.ExitRequested) { break; } // Ctrl+C mid-run: silent exit
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"错误: {ex.Message}");
                }
                finally { state.Busy = false; }
            }
        }
        finally
        {
            Console.WriteLine();   // land the shell prompt on a fresh line
        }

        return 0;
    }

    /// <summary>REPL command /rvt: inspect or switch the effective Revit model batch without
    /// involving the LLM. No args -> list the current batch with 1-based indices. Each spec
    /// resolves as: "all" (the entering batch), a 1-based index into the current list, an
    /// existing directory (scan its top-level *.rvt), an existing .rvt file, or a substring
    /// matched against the entering batch's file names (case-insensitive). Any unresolvable
    /// token leaves the batch unchanged and reports the failures.</summary>
    private static void HandleRvtCommand(string[] specs, AgentHost host)
    {
        var current = host.CurrentModelPaths;
        var initial = host.InitialModelPaths;

        if (specs.Length == 0)
        {
            // Real console (VT on + keyboard not redirected) → interactive picker; otherwise fall
            // back to the plain text list (piped / non-VT still works).
            if (ConsoleAnsi.Enabled && !Console.IsInputRedirected && initial.Count > 0)
            {
                var picked = PickModelsInteractive(initial, current);
                if (picked is null) { /* Esc: picker already cleared; keep current batch */ }
                else if (picked.Count == 0) { Console.WriteLine("未选择，保持当前。"); }
                else
                {
                    host.SetModelPaths(picked);
                    Console.WriteLine($"已切换到 {picked.Count} 个模型：");
                    foreach (var p in picked) Console.WriteLine($"  - {Path.GetFileName(p)}");
                    Console.WriteLine("后续提问只作用于这些模型（/rvt all 恢复全部）。");
                }
            }
            else
            {
                if (current.Count == 0) { Console.WriteLine("当前没有生效的模型。"); return; }
                Console.WriteLine($"当前生效模型（{current.Count}/{initial.Count}）：");
                for (var i = 0; i < current.Count; i++)
                    Console.WriteLine($"  {i + 1}. {Path.GetFileName(current[i])}");
                Console.WriteLine("用 /rvt <序号|文件名|路径|目录|all> 切换。");
            }
            return;
        }

        var resolved = new List<string>();
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
        {
            List<string> matched = new();
            if (spec.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                matched.AddRange(initial);
            }
            else if (int.TryParse(spec, out var idx) && idx >= 1)
            {
                // 1-based index into the CURRENT batch (what /rvt just listed).
                if (idx <= current.Count) matched.Add(current[idx - 1]);
                else { errors.Add($"序号 {idx} 超出范围（当前 {current.Count} 个）"); continue; }
            }
            else if (Directory.Exists(spec))
            {
                matched.AddRange(Directory.GetFiles(spec, "*.rvt").Select(Path.GetFullPath));
            }
            else if (File.Exists(spec))
            {
                matched.Add(Path.GetFullPath(spec));
            }
            else
            {
                // Substring match against the entering batch's file names (case-insensitive).
                var byName = initial
                    .Where(p => Path.GetFileName(p).Contains(spec, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (byName.Count == 0) { errors.Add($"未匹配到模型: {spec}"); continue; }
                matched.AddRange(byName);
            }

            foreach (var p in matched)
            {
                var abs = Path.GetFullPath(p);
                if (seen.Add(abs)) resolved.Add(abs);
            }
        }

        if (errors.Count > 0)
        {
            foreach (var e in errors) Console.Error.WriteLine(e);
            Console.Error.WriteLine("未切换（有无法解析的参数）。用 /rvt 查看当前清单。");
            return;
        }
        if (resolved.Count == 0)
        {
            Console.Error.WriteLine("未匹配到任何模型，保持当前。");
            return;
        }

        resolved.Sort(StringComparer.OrdinalIgnoreCase);
        host.SetModelPaths(resolved);
        Console.WriteLine($"已切换到 {resolved.Count} 个模型：");
        foreach (var p in resolved) Console.WriteLine($"  - {Path.GetFileName(p)}");
        Console.WriteLine("后续提问只作用于这些模型（/rvt all 恢复全部）。");
    }

    /// <summary>Interactive multi-model picker (claude-style). Lists every model in
    /// <paramref name="initial"/>, pre-checks those in <paramref name="current"/> (the active
    /// batch), and starts the cursor on the first checked one. Keys: ↑/↓ move, Space toggles
    /// [✓], A toggles all, Enter applies the checked set (returns it), Esc cancels (returns
    /// null). Only entered when ConsoleAnsi.Enabled and the keyboard is not redirected.</summary>
    private static List<string>? PickModelsInteractive(
        IReadOnlyList<string> initial, IReadOnlyList<string> current)
    {
        var n = initial.Count;
        if (n == 0) return null;
        var isChecked = new bool[n];
        for (var i = 0; i < n; i++)
            isChecked[i] = current.Any(c => string.Equals(c, initial[i], StringComparison.OrdinalIgnoreCase));

        var cursor = 0;
        for (var i = 0; i < n; i++) if (isChecked[i]) { cursor = i; break; }

        var drawn = 0;
        while (true)
        {
            DrawPicker(initial, isChecked, cursor, n, ref drawn);
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    cursor = (cursor - 1 + n) % n;
                    break;
                case ConsoleKey.DownArrow:
                    cursor = (cursor + 1) % n;
                    break;
                case ConsoleKey.Spacebar:
                    isChecked[cursor] = !isChecked[cursor];
                    break;
                case ConsoleKey.A:
                    var allOn = isChecked.All(x => x);
                    for (var i = 0; i < n; i++) isChecked[i] = !allOn;
                    break;
                case ConsoleKey.Enter:
                    ClearPicker(drawn);
                    var picked = new List<string>(n);
                    for (var i = 0; i < n; i++) if (isChecked[i]) picked.Add(initial[i]);
                    return picked;
                case ConsoleKey.Escape:
                    ClearPicker(drawn);
                    return null;
            }
        }
    }

    /// <summary>Redraw the picker in place: move up to the header line, clear to end of screen,
    /// then print the header (gray) + one row per model. The cursor row is reverse-video and
    /// padded to the console width so the highlight fills the whole line.</summary>
    private static void DrawPicker(IReadOnlyList<string> initial, bool[] isChecked, int cursor, int n, ref int drawn)
    {
        if (drawn > 0)
            Console.Write($"\x1b[{drawn - 1}A"); // cursor up to the header
        Console.Write("\r\x1b[J");               // col 0, clear to end of screen

        var width = Console.BufferWidth > 0 ? Console.BufferWidth : 80;
        Console.Write(Grey(TruncateToWidth("  切换模型  ↑↓移动  Space切换  Enter确认  Esc取消  A全选", width - 1)));
        Console.Write("\r\n");
        var budget = Math.Max(8, width - 9); // leave room for "[✓] NN. " prefix + 1-col margin
        for (var i = 0; i < n; i++)
        {
            var mark = isChecked[i] ? "[✓]" : "[ ]";
            var name = TruncateToWidth(Path.GetFileName(initial[i]), budget);
            var row = $"{mark} {i + 1}. {name}";
            if (i == cursor) row = PadToWidth(row, width - 1);
            Console.Write(i == cursor ? $"\x1b[7m{row}\x1b[0m" : row);
            if (i < n - 1) Console.Write("\r\n");
        }
        drawn = n + 1;
    }

    /// <summary>Move up to the picker header and clear to end of screen so the picker leaves no
    /// residue; the caller prints the result on the cleared line.</summary>
    private static void ClearPicker(int drawn)
    {
        if (drawn > 0)
            Console.Write($"\x1b[{drawn - 1}A\r\x1b[J");
    }

    private static string Grey(string s) =>
        ConsoleAnsi.Enabled ? $"\x1b[2;90m{s}\x1b[0m" : s;

    /// <summary>Truncate <paramref name="s"/> to fit <paramref name="budget"/> display columns
    /// (CJK-aware so full-width chars count as 2). Appends … when cut.</summary>
    private static string TruncateToWidth(string s, int budget)
    {
        if (budget <= 0) return string.Empty;
        var w = 0;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            var cw = CharWidth(ch);
            if (cw == 0) continue;
            if (w + cw > budget) { sb.Append('…'); break; }
            sb.Append(ch);
            w += cw;
        }
        return sb.ToString();
    }

    /// <summary>Pad <paramref name="s"/> with trailing spaces until its display width reaches
    /// <paramref name="budget"/>, so reverse-video covers the full line.</summary>
    private static string PadToWidth(string s, int budget)
    {
        var w = 0;
        foreach (var ch in s) w += CharWidth(ch);
        var pad = budget - w;
        return pad <= 0 ? s : s + new string(' ', pad);
    }

    // East-Asian full-width aware display width: CJK/全角 = 2, control = 0, else 1.
    // Mirrors ProcessDisplay.CharWidth (kept private there) so this file stays self-contained.
    private static int CharWidth(char ch)
    {
        var c = (int)ch;
        if (c == 0 || c < 0x20 || c == 0x7F) return 0;
        if (c >= 0x1100 && c <= 0x115F) return 2;
        if (c >= 0x2E80 && c <= 0x303E) return 2;
        if (c >= 0x3040 && c <= 0x33BF) return 2;
        if (c >= 0x3400 && c <= 0x4DBF) return 2;
        if (c >= 0x4E00 && c <= 0x9FFF) return 2;
        if (c >= 0xA000 && c <= 0xA4CF) return 2;
        if (c >= 0xAC00 && c <= 0xD7A3) return 2;
        if (c >= 0xF900 && c <= 0xFAFF) return 2;
        if (c >= 0xFE30 && c <= 0xFE4F) return 2;
        if (c >= 0xFF00 && c <= 0xFF60) return 2;
        if (c >= 0xFFE0 && c <= 0xFFE6) return 2;
        return 1;
    }

    /// <summary>Mutable REPL flags read by the Ctrl+C handler (threadpool thread) and written by
    /// the main loop. C# locals can't be volatile, so they live on a small heap object the handler
    /// closure captures by reference; the volatile fields make the cross-thread read/write visible
    /// without locking.</summary>
    private sealed class ReplState
    {
        public volatile bool Busy;
        public volatile bool ExitRequested;
    }
}
