using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Maki.Models;

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string _content = string.Empty;

    public required string Role { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public bool IsUser      => Role == "user";
    public bool IsAssistant => Role == "assistant";
}
