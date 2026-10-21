#nullable enable
using System;
using System.Collections.Generic;
using TerrariaChatGPT.Common.UI;

namespace TerrariaChatGPT.Tests;

public static class ReplyNotificationTests
{
    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            passed.Add(name);
        }
        var state = new ReplyNotificationState();
        object first = new(), second = new(), old = new();
        state.Observe(null, 1, false, 10);
        Check(!state.HasUnread && state.PreviewOpacity(10) == 0, "Starting an empty conversation creates no notification.");
        state.Observe(first, 1, false, 20);
        Check(state.HasUnread && ReferenceEquals(state.PreviewReply, first), "A completed reply while chat is closed becomes unread.");
        Check(state.PreviewOpacity(20) == 0 && state.PreviewOpacity(20.1) > 0 && state.PreviewOpacity(20.1) < 1,
            "Preview gently fades in instead of flashing.");
        Check(state.PreviewOpacity(20.25) == 1 && state.PreviewOpacity(23.9) == 1,
            "Preview holds long enough to read its short excerpt.");
        float early = state.PreviewOpacity(24.5), late = state.PreviewOpacity(26.5);
        Check(early > late && late > 0 && early < 1, "Preview gradually fades out.");
        Check(state.PreviewOpacity(27) == 0 && state.HasUnread,
            "Unread indicator persists after the seven-second preview fades.");
        state.Observe(first, 1, false, 40);
        Check(state.PreviewOpacity(40) == 0, "Repeated frames and a stable final history row cannot restart the preview.");
        state.Observe(second, 1, false, 50);
        Check(ReferenceEquals(state.PreviewReply, second) && state.PreviewOpacity(51) == 1,
            "A new reply restarts the preview even when the history count is unchanged.");
        state.Observe(second, 1, true, 51);
        Check(!state.HasUnread && state.PreviewReply is null && state.PreviewOpacity(51) == 0,
            "Opening GPT immediately marks the reply read and dismisses its preview.");
        state.Observe(second, 1, false, 52);
        Check(!state.HasUnread, "Closing GPT again does not re-notify the answer just read.");
        state.Observe(first, 1, true, 53);
        state.Observe(first, 1, false, 54);
        Check(!state.HasUnread, "Replies arriving while GPT is already open stay read.");
        state.Observe(second, 1, false, 55);
        state.MarkRead();
        Check(!state.HasUnread && state.PreviewReply is null, "An explicit GPT-tab selection clears unread immediately.");
        state.Observe(first, 1, false, 56);
        state.Observe(first, 2, false, 57);
        Check(!state.HasUnread, "Changing provider/privacy epoch clears unread even if visible history is retained.");
        state.Observe(second, 2, false, 58);
        Check(state.HasUnread, "A fresh completion after an epoch change can notify normally.");
        state.Reset();
        Check(!state.HasUnread && state.PreviewReply is null, "World unload/reset drops unread and retained preview references.");
        state.Observe(old, 3, false, 60);
        Check(!state.HasUnread, "Binding to existing history does not treat old answers as new.");
        state.Observe(first, 3, false, 61);
        Check(state.HasUnread && state.PreviewOpacity(double.NaN) == 0 && state.PreviewOpacity(double.PositiveInfinity) == 0,
            "Invalid clocks cannot produce invalid opacity or interfere with unread state.");
        Check(state.PreviewOpacity(50) == 0, "A clock before arrival stays within the fade-in bounds.");
        state.Observe(null, 4, false, 80);
        Check(!state.HasUnread && state.PreviewReply is null, "Clearing chat clears the notification with its history.");
        return passed;
    }
}
