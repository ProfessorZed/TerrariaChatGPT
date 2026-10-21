#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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

public static class ModelSelectorTests
{
    private static readonly IReadOnlyList<ChatGptModelOption> Catalog = new[] {
        new ChatGptModelOption("first", "First model"), new ChatGptModelOption("second", "Second model"),
        new ChatGptModelOption("third", "Third model"), new ChatGptModelOption("fourth", "Fourth model"),
        new ChatGptModelOption("fifth", "Fifth model") };

    public static IReadOnlyList<string> Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task<IReadOnlyList<string>> RunAsync()
    {
        var results = new List<string>();
        using (var f = new Fixture())
        {
            f.Pump(); f.Click(20, 45); f.Pump(); f.Click(350, 80); f.Pump();
            Equal(0, NativeAssistantRuntime.Calls);
            results.Add("Opening, expanding and choosing Auto never requests a catalog.");
        }
        foreach (bool pending in new[] { false, true })
        {
            foreach (GptProvider provider in new[] { GptProvider.WikiOnly, GptProvider.OpenAiApi })
            {
                using var f = new Fixture();
                (pending ? f.Draft : ModContent.Actual).Provider = provider;
                f.Click(20, 80); f.Pump();
                Equal(0, NativeAssistantRuntime.Calls);
                Require(Field<string>(f.Picker, "_feedback").Contains("ChatGPT plan"));
                results.Add($"{(pending ? "Draft" : "Active")} {provider} blocks account-model requests.");
            }
            using (var f = new Fixture())
            {
                (pending ? f.Draft : ModContent.Actual).EnableOnlineAnswers = false;
                f.Click(20, 80); f.Pump(); Equal(0, NativeAssistantRuntime.Calls);
                results.Add($"{(pending ? "Draft" : "Active")} offline setting blocks catalog requests.");
            }
        }
        using (var f = new Fixture())
        {
            await f.Load(); Equal(1, NativeAssistantRuntime.Calls); Require(NativeAssistantRuntime.LastForceRefresh);
            Equal("first,second,third,fourth,fifth", string.Join(",", f.Models.Select(model => model.Slug)));
            f.Click(20, 175); Equal("second", f.Draft.ChatGptModel); Equal("", ModContent.Actual.ChatGptModel); Equal(1, f.Picker.Writes);
            Require(Field<string>(f.Picker, "_feedback").Contains("Save Config"));
            results.Add("Explicit load preserves catalog order; choosing a model changes only the pending config.");
            f.Click(20, 45); f.Click(400, 270); f.Click(20, 150);
            Equal("fifth", f.Draft.ChatGptModel);
            results.Add("Paging reaches later catalog entries without changing server order.");
            f.Click(350, 80); Equal("", f.Draft.ChatGptModel); Equal(1, NativeAssistantRuntime.Calls);
            results.Add("Use Auto clears an explicit draft choice without another network request.");
        }
        using (var f = new Fixture())
        {
            f.Draft.ChatGptModel = "no-longer-available";
            await f.Load();
            Equal("no-longer-available", f.Draft.ChatGptModel); Require(Field<bool>(f.Picker, "_error"));
            Require(Field<string>(f.Picker, "_feedback").Contains("isn't available"));
            results.Add("Unavailable saved choices are retained with an actionable warning, never silently replaced.");
        }
        using (var f = new Fixture())
        {
            NativeAssistantRuntime.Handler = _ => throw new InvalidOperationException("FAKE-SECRET-must-not-reach-ui");
            await f.Load();
            Require(!Field<string>(f.Picker, "_feedback").Contains("FAKE-SECRET"));
            Require(Field<bool>(f.Picker, "_error"));
            f.Draft.ChatGptModel = "second"; f.Click(350, 80); Equal("", f.Draft.ChatGptModel);
            results.Add("Unexpected failure details are hidden and Auto remains selectable after failure.");
        }
        foreach (bool epoch in new[] { false, true })
        {
            using var f = new Fixture();
            var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<IReadOnlyList<ChatGptModelOption>>(TaskCreationOptions.RunContinuationsAsynchronously);
            NativeAssistantRuntime.Handler = token => { started.TrySetResult(token); return finish.Task; };
            f.Click(20, 80);
            Task oldRequest = Pending(f.Picker);
            CancellationToken token = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            f.Click(20, 80); Equal(1, NativeAssistantRuntime.Calls);
            if (epoch) { GptAssistantService.Epoch++; f.Pump(); }
            else f.Picker.OnDeactivate();
            Require(token.IsCancellationRequested);
            finish.SetResult(Catalog); await oldRequest; f.Pump(); Equal(0, f.Models.Count);
            if (!epoch) f.Picker.OnActivate();
            NativeAssistantRuntime.Handler = _ => Task.FromResult<IReadOnlyList<ChatGptModelOption>>(new[] { new ChatGptModelOption("new-account", "New account model") });
            await f.Load(); Equal("new-account", f.Models[0].Slug);
            results.Add($"{(epoch ? "Account epoch change" : "Closing settings")} cancels loading and drops late results; a fresh load works.");
        }
        using (var f = new Fixture())
        {
            await f.Load(); f.Draft.EnableOnlineAnswers = false; f.Pump(); Equal(0, f.Models.Count);
            results.Add("Disabling online answers clears a previously loaded account list.");
        }
        using (var f = new Fixture())
        {
            f.Clip.Height.Set(70, 0); f.Click(20, 80); Equal(0, NativeAssistantRuntime.Calls);
            f.Clip.Height.Set(700, 0); await f.Load();
            f.Clip.Height.Set(160, 0); f.Click(20, 175); Equal("", f.Draft.ChatGptModel);
            results.Add("Rows and buttons clipped by the scrolling settings viewport cannot be clicked.");
        }
        return results;
    }

    private sealed class Fixture : IDisposable
    {
        public GptClientConfig Draft { get; } = new();
        public ChatGptModelConfigElement Picker { get; } = new();
        public UIElement Clip { get; } = new() { OverflowHidden = true };
        public IReadOnlyList<ChatGptModelOption> Models => Field<IReadOnlyList<ChatGptModelOption>>(Picker, "_models");
        public Fixture()
        {
            ModContent.Actual = new(); GptAssistantService.Epoch = 0; NativeAssistantRuntime.Calls = 0;
            NativeAssistantRuntime.Handler = _ => Task.FromResult(Catalog);
            Main.gameMenu = true;
            var root = new UIState(); Main.MenuUI.CurrentState = root;
            Clip.Parent = root;
            var wrapper = new UIElement { Parent = Clip };
            Picker.Parent = wrapper; Picker.Bind(Draft); Picker.OnBind(); Picker.OnActivate();
        }
        public void Click(int x, int y) => Picker.LeftMouseDown(new UIMouseEvent(x, y));
        public void Pump() => Picker.Update(new GameTime());
        public async Task Load()
        {
            Click(20, 80); await Pending(Picker).WaitAsync(TimeSpan.FromSeconds(3)); Pump();
        }
        public void Dispose() => Picker.OnDeactivate();
    }
    private static Task Pending(ChatGptModelConfigElement picker) => Field<Task>(picker, "_pending");
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}.");
    }
    private static void Require(bool condition) { if (!condition) throw new Exception("Model selector interaction assertion failed."); }
}
