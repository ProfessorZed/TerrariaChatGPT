#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Native;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.UI;

namespace TerrariaChatGPT.Tests;

public static class EffortSelectorTests
{
    private static readonly IReadOnlyList<ChatGptModelOption> Catalog = new[] {
        new ChatGptModelOption("first", "First model") { SupportedReasoningEfforts = new[] {"low", "medium", "high"}, DefaultReasoningEffort = "medium" },
        new ChatGptModelOption("second", "Second model") { SupportedReasoningEfforts = new[] {"none", "low"}, DefaultReasoningEffort = "none" },
        new ChatGptModelOption("no-metadata", "Model default only")
    };

    public static IReadOnlyList<string> Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task<IReadOnlyList<string>> RunAsync()
    {
        var results = new List<string>();
        using (var f = new Fixture())
        {
            f.Pump(); f.Click(20, 45); f.Pump(); f.Click(350, 80); f.Pump();
            Equal(0, NativeAssistantRuntime.Calls);
            results.Add("Opening, expanding and choosing Auto effort never sends a catalog request.");
        }
        foreach (bool pending in new[] {false, true})
        {
            foreach (GptProvider provider in new[] {GptProvider.WikiOnly, GptProvider.OpenAiApi})
            {
                using var f = new Fixture();
                (pending ? f.Draft : ModContent.Actual).Provider = provider;
                f.Click(20, 80); f.Pump(); Equal(0, NativeAssistantRuntime.Calls);
                results.Add($"{(pending ? "Draft" : "Active")} {provider} blocks effort-catalog requests.");
            }
            using (var f = new Fixture())
            {
                (pending ? f.Draft : ModContent.Actual).EnableOnlineAnswers = false;
                f.Click(20, 80); f.Pump(); Equal(0, NativeAssistantRuntime.Calls);
                results.Add($"{(pending ? "Draft" : "Active")} offline preference blocks effort-catalog requests.");
            }
        }
        using (var f = new Fixture())
        {
            await f.Load();
            Equal("low,medium,high", string.Join(",", f.Choices));
            f.Click(350, 150); Equal("medium", f.Draft.ChatGptEffort); Equal("", ModContent.Actual.ChatGptEffort);
            Equal("", f.Draft.ChatGptModel); Equal(1, f.Picker.Writes);
            Require(Field<string>(f.Picker, "_feedback").Contains("Save Config"));
            results.Add("Effort choices follow the Auto model; selection updates only the draft effort, not the model or active config.");
            f.Draft.ChatGptModel = "second"; f.Pump();
            Equal("none,low", string.Join(",", f.Choices));
            Equal("medium", f.Draft.ChatGptEffort); Require(Field<bool>(f.Picker, "_error"));
            Equal(1, NativeAssistantRuntime.Calls);
            results.Add("Changing the pending model updates supported efforts immediately and warns about an incompatible saved effort.");
            f.Click(20, 45); f.Click(20, 150); Equal("none", f.Draft.ChatGptEffort);
            results.Add("Instant is selectable only when the pending model supports none.");
            f.Draft.ChatGptModel = "first";
            // No update frame: hit testing still synchronizes the pending model.
            f.Click(20, 45); f.Click(20, 150); Equal("low", f.Draft.ChatGptEffort);
            results.Add("A model change between update and click cannot select a stale unsupported effort.");
            f.Click(350, 80); Equal("", f.Draft.ChatGptEffort); Equal(1, NativeAssistantRuntime.Calls);
            results.Add("Use Auto clears the effort override without another request.");
        }
        using (var f = new Fixture())
        {
            f.Draft.ChatGptModel = "no-metadata"; await f.Load(); Equal(0, f.Choices.Count);
            f.Draft.ChatGptEffort = "high"; f.Click(350, 80); Equal("", f.Draft.ChatGptEffort);
            results.Add("Missing model effort metadata leaves Auto available and invents no effort choices.");
        }
        using (var f = new Fixture())
        {
            f.Draft.ChatGptModel = "not-in-catalog"; await f.Load(); Equal(0, f.Choices.Count);
            Require(Field<bool>(f.Picker, "_error")); Equal("not-in-catalog", f.Draft.ChatGptModel);
            results.Add("An unavailable selected model is reported without silently selecting another model.");
        }
        using (var f = new Fixture())
        {
            NativeAssistantRuntime.Handler = _ => Task.FromResult<IReadOnlyList<ChatGptModelOption>>(new[] {
                new ChatGptModelOption("first", "Synthetic model") { SupportedReasoningEfforts = new[] {"low", "unexpected", "low", "", "medium"} }
            });
            await f.Load(); Equal("low,medium", string.Join(",", f.Choices));
            results.Add("Unknown and duplicate effort metadata cannot become selectable UI entries.");
        }
        using (var f = new Fixture())
        {
            NativeAssistantRuntime.Handler = _ => throw new InvalidOperationException("FAKE-PRIVATE-DATA");
            await f.Load(); Require(!Field<string>(f.Picker, "_feedback").Contains("FAKE-PRIVATE"));
            Require(Field<bool>(f.Picker, "_error"));
            results.Add("Unexpected errors never expose raw exception details.");
        }
        foreach (bool epoch in new[] {false, true})
        {
            using var f = new Fixture();
            var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<IReadOnlyList<ChatGptModelOption>>(TaskCreationOptions.RunContinuationsAsynchronously);
            NativeAssistantRuntime.Handler = token => { started.TrySetResult(token); return finish.Task; };
            f.Click(20, 80); Task oldRequest = Field<Task>(f.Picker, "_pending");
            CancellationToken token = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            f.Click(20, 80); Equal(1, NativeAssistantRuntime.Calls);
            if (epoch) { GptAssistantService.Epoch++; f.Pump(); } else f.Picker.OnDeactivate();
            Require(token.IsCancellationRequested);
            finish.SetResult(Catalog); await oldRequest; f.Pump(); Equal(0, f.Choices.Count);
            if (!epoch) f.Picker.OnActivate();
            NativeAssistantRuntime.Handler = _ => Task.FromResult(Catalog);
            await f.Load(); Equal(3, f.Choices.Count);
            results.Add($"{(epoch ? "Account change" : "Deactivation")} cancels effort loading, drops late results, and permits a fresh load.");
        }
        using (var f = new Fixture())
        {
            await f.Load(); f.Draft.EnableOnlineAnswers = false; f.Pump(); Equal(0, f.Choices.Count);
            results.Add("Disabling online answers clears loaded account effort choices.");
        }
        using (var f = new Fixture())
        {
            f.Clip.Height.Set(70,0); f.Click(20,80); Equal(0, NativeAssistantRuntime.Calls);
            f.Clip.Height.Set(700,0); await f.Load();
            f.Clip.Height.Set(135,0); f.Click(20,150); Equal("",f.Draft.ChatGptEffort);
            results.Add("Effort controls clipped by the settings viewport cannot be clicked.");
        }
        return results;
    }

    private sealed class Fixture : IDisposable
    {
        public GptClientConfig Draft { get; } = new();
        public ChatGptEffortConfigElement Picker { get; } = new();
        public UIElement Clip { get; } = new() { OverflowHidden = true };
        public IReadOnlyList<string> Choices => Field<IReadOnlyList<string>>(Picker, "_choices");
        public Fixture()
        {
            ModContent.Actual = new(); GptAssistantService.Epoch = 0; NativeAssistantRuntime.Calls = 0;
            NativeAssistantRuntime.Handler = _ => Task.FromResult(Catalog);
            Main.gameMenu = true;
            var root = new UIState(); Main.MenuUI.CurrentState = root; Clip.Parent = root;
            Picker.Parent = new UIElement { Parent = Clip }; Picker.Bind(Draft, "ChatGptEffort"); Picker.OnBind(); Picker.OnActivate();
        }
        public void Click(int x,int y) => Picker.LeftMouseDown(new UIMouseEvent(x,y));
        public void Pump() => Picker.Update(new GameTime());
        public async Task Load() { Click(20,80); await Field<Task>(Picker,"_pending").WaitAsync(TimeSpan.FromSeconds(3)); Pump(); }
        public void Dispose() => Picker.OnDeactivate();
    }
    private static T Field<T>(object obj,string name) => (T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(obj)!;
    private static void Equal<T>(T expected,T actual) { if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Require(bool condition) { if (!condition) throw new Exception("Effort selector interaction assertion failed."); }
}
