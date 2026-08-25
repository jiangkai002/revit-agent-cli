namespace RevitAgent.Gui.Models;

/// <summary>
/// Marshaled form of the <see cref="RevitAgent.Cli.IAgentTurnDisplay"/> events, posted by
/// GuiAgentDisplay to the UI thread. One record per display callback; the ChatViewModel turns
/// them into ChatItems / stage-bar updates.
/// </summary>
public abstract record TurnEvent;

public sealed record StageEvent(string Stage) : TurnEvent;

public sealed record ExecutingEvent : TurnEvent;

public sealed record ReasoningDeltaEvent(string Chunk) : TurnEvent;

public sealed record PreambleEvent(string Text) : TurnEvent;

public sealed record ToolCallEvent(string Name, string ArgumentsText) : TurnEvent;

public sealed record ToolResultEvent(string ResultText) : TurnEvent;

public sealed record TurnCompletedEvent : TurnEvent;

/// <summary>Broadcast (WeakReferenceMessenger) after a skill install/remove so the chat page
/// can hint that a new conversation is needed (the agent's skills catalog is frozen at first
/// agent build).</summary>
public sealed class SkillsChangedMessage : CommunityToolkit.Mvvm.Messaging.Messages.ValueChangedMessage<bool>
{
    public SkillsChangedMessage() : base(true) { }
}
