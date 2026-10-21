#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TerrariaChatGPT.Common.Native;
using TerrariaChatGPT.Common.UI;

namespace TerrariaChatGPT.Tests;

public static class ChatToolbarTests
{
    public static string[] Run()
    {
        var passed = new List<string>();
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            passed.Add(description);
        }
        ChatGptModelOption[] models = {
            new("model-a", "Model A") { SupportedReasoningEfforts = new[] { "low", "medium", "high" }, DefaultReasoningEffort = "low" },
            new("model-b", "Model B") { SupportedReasoningEfforts = new[] { "none", "low" } },
            new("model-c", "Model C")
        };
        int loads = 0, saves = 0;
        bool lastForced = false, saveAllowed = true;
        string savedModel = "model-a", savedEffort = "high";
        Func<CancellationToken, bool, Task<IReadOnlyList<ChatGptModelOption>>> loader = (_, force) => {
            Interlocked.Increment(ref loads); lastForced = force; return Task.FromResult<IReadOnlyList<ChatGptModelOption>>(models);
        };
        bool Save(string model, string effort, out string message)
        {
            saves++; message = saveAllowed ? "Saved. Applies to your next question." : "Couldn't save this choice. Try again.";
            if (!saveAllowed) return false;
            savedModel = model; savedEffort = effort; return true;
        }
        var picker = new ChatModelToolbarController((ct, force) => loader(ct, force), Save);
        void Update(bool open = true, bool enabled = true, int epoch = 1) => picker.Update(open, enabled, epoch, savedModel, savedEffort);
        void Finish(int epoch = 1)
        {
            if (!SpinWait.SpinUntil(() => { Update(epoch: epoch); return !picker.Busy; }, 5000))
                throw new InvalidOperationException("Mock catalog did not complete.");
        }
        Update(); Update();
        Check(loads == 0 && saves == 0 && !picker.IsOpen, "Opening GPT chat leaves catalog requests and saved settings untouched.");
        picker.Toggle(ChatPickerKind.Model); Finish();
        Check(loads == 1 && !lastForced && picker.IsOpen, "Opening model selector performs one cached-capable catalog request.");
        Check(picker.Choices().Select(choice => choice.Value).SequenceEqual(new[] { "", "model-a", "model-b", "model-c" }), "Model options preserve account order with Auto first.");
        picker.Toggle(ChatPickerKind.Effort);
        Check(picker.Choices().Select(choice => choice.Value).SequenceEqual(new[] { "", "low", "medium", "high" }), "Effort options include only the current model's supported levels.");
        Check(picker.Choices()[0].Label == "Auto — service default", "Auto does not promise the catalog's potentially different default effort.");
        picker.Select(1);
        Check(savedEffort == "low" && savedModel == "model-a" && !picker.IsOpen && saves == 1, "Selecting effort saves immediately and closes the selector.");
        Update(); picker.Toggle(ChatPickerKind.Model); picker.Select(2);
        Check(savedModel == "model-b" && savedEffort == "low", "Switching models preserves a supported effort.");
        Update(); picker.Toggle(ChatPickerKind.Effort);
        Check(picker.Choices().Select(choice => choice.Value).SequenceEqual(new[] { "", "none", "low" }), "Instant appears only for a model that advertises none.");
        picker.Select(1); Update(); picker.Toggle(ChatPickerKind.Model); picker.Select(1);
        Check(savedModel == "model-a" && savedEffort == "" && picker.Feedback.Contains("Auto", StringComparison.Ordinal), "Switching away from unsupported Instant resets effort to Auto with an explicit notice.");
        Update(); picker.Toggle(ChatPickerKind.Model); picker.Select(3); Update(); picker.Toggle(ChatPickerKind.Effort);
        Check(picker.Choices().Count == 1, "A model without effort metadata exposes only Auto.");
        picker.Select(0); Update(); picker.Toggle(ChatPickerKind.Model);
        saveAllowed = false; int before = saves; picker.Select(1);
        Check(saves == before + 1 && savedModel == "model-c" && picker.IsOpen && picker.Error, "Failed persistence keeps previous selection and an actionable open-menu error.");
        saveAllowed = true;
        picker.ChangePage(1, 2);
        Check(picker.Page == 1, "Pagination reaches later models.");
        picker.ChangePage(50, 2); Check(picker.Page == 1, "Pagination clamps at the final page.");
        picker.ChangePage(-50, 2); Check(picker.Page == 0, "Pagination clamps at the first page.");
        picker.Refresh(); picker.Refresh(); Finish();
        Check(loads == 2 && lastForced, "Refresh explicitly bypasses the catalog cache without overlapping requests.");
        picker.Close(); Update(false); Update(); picker.Toggle(ChatPickerKind.Model);
        Check(loads == 2 && !picker.Busy, "Closing and reopening chat reuses loaded public choices.");
        picker.Close(); Update(enabled: false); picker.Toggle(ChatPickerKind.Model); picker.Refresh();
        Check(loads == 2 && !picker.IsOpen, "Non-ChatGPT and offline contexts cannot open or fetch account-model menus.");

        var late = new TaskCompletionSource<IReadOnlyList<ChatGptModelOption>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken requestToken = default;
        var arrived = new ManualResetEventSlim();
        loader = (ct, _) => { requestToken = ct; arrived.Set(); return late.Task; };
        Update(); picker.Toggle(ChatPickerKind.Model);
        Check(arrived.Wait(5000), "Synthetic delayed catalog entered the background worker.");
        Update(false);
        Check(requestToken.IsCancellationRequested && !picker.Busy && !picker.IsOpen, "Closing GPT cancels in-flight loading.");
        late.SetResult(models); Update();
        Check(picker.ModelLabel == "model-c", "Late results after chat closes cannot refill dropped catalog state.");
        var accountLate = new TaskCompletionSource<IReadOnlyList<ChatGptModelOption>>(TaskCreationOptions.RunContinuationsAsynchronously);
        arrived.Reset(); loader = (ct, _) => { requestToken = ct; arrived.Set(); return accountLate.Task; };
        picker.Toggle(ChatPickerKind.Model); arrived.Wait(5000); Update(epoch: 2);
        Check(requestToken.IsCancellationRequested && !picker.IsOpen && !picker.Busy, "Account/conversation epoch changes close menus and cancel old-account loads.");
        accountLate.SetResult(models); Update(epoch: 2);
        Check(picker.ModelLabel == "model-c", "Old-account results remain rejected after completion.");
        loader = (_, _) => throw new Exception("private credential marker");
        picker.Toggle(ChatPickerKind.Model); Finish(2);
        Check(picker.Error && !picker.Feedback.Contains("credential", StringComparison.Ordinal) && picker.Choices().Count == 1,
            "Unexpected catalog failure hides exception details and retains Auto.");
        picker.Select(0);
        Check(savedModel == "" && savedEffort == "", "Auto remains usable after a model-list failure.");

        foreach ((int width, int height) in new[] { (1, 1), (80, 50), (320, 180), (320, 240), (640, 360), (800, 600), (1280, 720), (1920, 1080) })
        {
            foreach (int preferred in new[] { 240, 390, 720 })
            {
                ChatPanelGeometry g = ChatPanelGeometry.Calculate(width, height, 820, preferred);
                foreach (ChatRect r in new[] { g.ModelSelector, g.EffortSelector })
                    if (r.Width > 0 && r.Height > 0 && (r.X < 0 || r.Y < 0 || r.Right > width || r.Bottom > height || r.Bottom > g.Transcript.Y))
                        throw new InvalidOperationException("Toolbar crossed a viewport or transcript boundary.");
                var popup = ChatToolbarPopupLayout.Calculate(g.Panel, g.ModelSelector, g.EffortSelector);
                if (popup.Rows == 0) continue;
                foreach (ChatRect r in new[] { popup.Bounds,popup.Refresh,popup.Close,popup.Previous,popup.Next,popup.Page,popup.Status }
                    .Concat(Enumerable.Range(0,popup.Rows).Select(popup.Row)))
                    if (r.X < g.Panel.X || r.Y < g.Panel.Y || r.Width < 0 || r.Height < 0 || r.Right > g.Panel.Right || r.Bottom > g.Panel.Bottom)
                        throw new InvalidOperationException("Popup control escaped its containing panel.");
            }
        }
        Check(true, "Toolbar and popup controls fit small/large viewports and all supported panel heights without overlapping the transcript header.");
        return passed.ToArray();
    }
}
