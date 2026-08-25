using CommunityToolkit.Mvvm.ComponentModel;

namespace RevitAgent.Gui.Models;

/// <summary>
/// One entry in the chat transcript. A discriminated-union-ish hierarchy: each subclass gets
/// its own implicit DataTemplate in ChatPage.xaml. Immutable except where live streaming
/// requires updates (ReasoningSection). Inherits ObservableObject so mutable subclasses get
/// INPC for free.
/// </summary>
public abstract class ChatItem : ObservableObject;

/// <summary>The user's message, rendered right-aligned.</summary>
public sealed class UserMessage(string text) : ChatItem
{
    public string Text { get; } = text;
}

/// <summary>The assistant's final answer for a turn, rendered as markdown.</summary>
public sealed class AssistantMessage(string markdown) : ChatItem
{
    public string Markdown { get; } = markdown;
}

/// <summary>
/// A live-streamed chain-of-thought segment ("thinking process"). Deltas append to
/// <see cref="Text"/>; a following tool call/result closes the segment (a later delta opens a
/// new one). Collapsed by default with a live one-line preview in the header so the transcript
/// stays compact while still showing activity.
/// </summary>
public sealed class ReasoningSection : ChatItem
{
    private string _text = "";

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
                OnPropertyChanged(nameof(Preview));
        }
    }

    /// <summary>Live single-line preview for the collapsed header (first 60 chars).</summary>
    public string Preview
    {
        get
        {
            var t = Text.ReplaceLineEndings(" ");
            return t.Length <= 60 ? t : t[..60] + "…";
        }
    }
}

/// <summary>A tool invocation: one-line preview, expandable to the FULL arguments (e.g. the
/// complete generated C# source — the GUI's advantage over the console's 80-char truncation).</summary>
public sealed class ToolCallItem(string name, string argumentsText) : ChatItem
{
    public string Name { get; } = name;
    public string ArgumentsText { get; } = argumentsText;
    public int Length => ArgumentsText.Length;
}

/// <summary>A tool result: one-line preview, expandable to the full (pretty-printed) text.</summary>
public sealed class ToolResultItem(string resultText) : ChatItem
{
    public string ResultText { get; } = resultText;
    public int Length => ResultText.Length;
}

/// <summary>Dim informational line: assistant preamble, 已取消, hints.</summary>
public sealed class InfoItem(string text) : ChatItem
{
    public string Text { get; } = text;
}

/// <summary>Error line shown in the transcript (missing API key, turn failure, …).</summary>
public sealed class ErrorMessage(string text) : ChatItem
{
    public string Text { get; } = text;
}
