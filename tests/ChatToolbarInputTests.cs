#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Native;
using TerrariaChatGPT.Common.UI;

namespace Microsoft.Xna.Framework
{
    public readonly record struct Point(int X, int Y);
    public readonly record struct Vector2(float X, float Y);
    public readonly record struct Rectangle(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width; public int Bottom => Y + Height;
        public bool Contains(Point p) => p.X >= X && p.Y >= Y && p.X < Right && p.Y < Bottom;
    }
    public readonly record struct Color(int R, int G, int B, int A = 255);
}
namespace Microsoft.Xna.Framework.Graphics { public sealed class SpriteBatch { } }
namespace Terraria.GameContent
{
    public sealed class Font { public float CharacterSpacing => 0; }
    public sealed class Asset { public Font Value = new(); }
    public static class FontAssets { public static Asset MouseText = new(); }
}
namespace TerrariaChatGPT.Common.Config
{
    public enum GptProvider { ChatGPT, WikiOnly, OpenAiApi }
    public sealed class GptClientConfig
    { public GptProvider Provider; public bool EnableOnlineAnswers = true; public string ChatGptModel = "model-0", ChatGptEffort = ""; }
}
namespace TerrariaChatGPT.Common.Native
{
    public static class NativeAssistantRuntime
    {
        public static int Calls;
        public static Task<IReadOnlyList<ChatGptModelOption>> GetModelsAsync(CancellationToken cancellation, bool forceRefresh = false)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult<IReadOnlyList<ChatGptModelOption>>(Enumerable.Range(0, 9).Select(i =>
                new ChatGptModelOption("model-" + i, "Model " + i) { SupportedReasoningEfforts = new[] { "low", "medium", "high" } }).ToArray());
        }
    }
}
namespace TerrariaChatGPT.Common.Services
{
    public static class GptChatPreferences
    {
        public static GptClientConfig Config = new(); public static int Saves;
        public static bool TrySave(string model, string effort, out string message)
        { Saves++; Config.ChatGptModel = model; Config.ChatGptEffort = effort; message = "Saved."; return true; }
    }
}
namespace TerrariaChatGPT.Common.UI
{
    internal readonly record struct GptChatLayout(Rectangle Panel, Rectangle Transcript, Rectangle Input,
        Rectangle TerrariaTab, Rectangle GptTab, Rectangle ConnectionBadge, Rectangle ManageUsage, Rectangle CopyAnswer,
        Rectangle ModelSelector, Rectangle EffortSelector);
    internal static class GptChatPanel
    {
        public static ChatGlyphMetrics Metrics(Terraria.GameContent.Font font, char c) => new(0, 8, 0);
        public static void DrawButton(SpriteBatch batch, Rectangle r, string text, bool enabled, float scale) { }
        public static void DrawBox(SpriteBatch batch, Rectangle r, Color fill, Color border) { }
        public static void DrawClippedText(SpriteBatch batch, string text, Vector2 p, Color color, float scale, Rectangle clip) { }
    }
}
namespace TerrariaChatGPT.Tests
{
    public static class ChatToolbarInputTests
    {
        public static string[] Run()
        {
            var output = new List<string>();
            void Check(bool condition, string label) { if (!condition) throw new Exception(label); output.Add(label); }
            static Rectangle R(ChatRect r) => new(r.X, r.Y, r.Width, r.Height);
            static Point P(Rectangle r) => new(r.X + 3, r.Y + 3);
            static GptChatLayout Layout(int width)
            {
                var g = ChatPanelGeometry.Calculate(width, 720, 820, 390);
                return new(R(g.Panel), R(g.Transcript), R(g.Input), R(g.TerrariaTab), R(g.GptTab), R(g.ConnectionBadge),
                    R(g.ManageUsage), R(g.CopyAnswer), R(g.ModelSelector), R(g.EffortSelector));
            }
            var toolbar = new ChatModelToolbar(); var layout = Layout(1280); var config = new GptClientConfig();
            TerrariaChatGPT.Common.Services.GptChatPreferences.Config = config;
            TerrariaChatGPT.Common.Services.GptChatPreferences.Saves = 0;
            NativeAssistantRuntime.Calls = 0;
            var batch = new SpriteBatch();
            toolbar.Update(true, config, 1); toolbar.Draw(batch, layout);
            Check(NativeAssistantRuntime.Calls == 0, "Rendering the chat toolbar does not contact the account.");
            Check(toolbar.HandleInput(layout, P(layout.ModelSelector), true, 0, false, 0) && toolbar.IsOpen, "Model-button click opens the menu and is consumed before transcript actions.");
            var controller = (ChatModelToolbarController)typeof(ChatModelToolbar).GetField("_controller", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(toolbar)!;
            if (!SpinWait.SpinUntil(() => { toolbar.Update(true, config, 1); return !controller.Busy; }, 5000)) throw new Exception("Mock load timed out.");
            toolbar.Draw(batch, layout);
            Check(toolbar.HandleInput(layout, P(layout.Transcript), false, -120, false, 0) && controller.Page == 1, "Wheel input pages the dropdown instead of passing through to chat scrolling.");
            Check(toolbar.HandleInput(layout, new Point(0, 0), true, 0, false, 0) && !toolbar.IsOpen, "Outside click dismisses the dropdown and cannot click through.");
            toolbar.HandleInput(layout, P(layout.EffortSelector), true, 0, false, 0); toolbar.Draw(batch, layout);
            Check(toolbar.HandleInput(layout, new Point(0, 0), false, 0, true, 0) && !toolbar.IsOpen
                && TerrariaChatGPT.Common.Services.GptChatPreferences.Saves == 0, "Escape dismisses a dropdown without saving or submitting anything.");
            toolbar.HandleInput(layout, P(layout.EffortSelector), true, 0, false, 0); toolbar.Draw(batch, layout);
            var g = ChatPanelGeometry.Calculate(1280, 720, 820, 390);
            var popup = ChatToolbarPopupLayout.Calculate(g.Panel, g.ModelSelector, g.EffortSelector);
            Check(toolbar.HandleInput(layout, P(R(popup.Row(1))), true, 0, false, 0) && config.ChatGptEffort == "low" && !toolbar.IsOpen,
                "Clicking Fast saves that effort and closes the popup.");
            toolbar.HandleInput(layout, P(layout.ModelSelector), true, 0, false, 0);
            int saves = TerrariaChatGPT.Common.Services.GptChatPreferences.Saves;
            var resized = Layout(640);
            Check(toolbar.HandleInput(resized, P(resized.ModelSelector), true, 0, false, 0)
                && TerrariaChatGPT.Common.Services.GptChatPreferences.Saves == saves, "A resize consumes stale popup clicks without changing selection.");
            toolbar.Update(false, config, 1);
            Check(!toolbar.IsOpen, "Closing GPT closes its dropdown too.");
            config.Provider = GptProvider.WikiOnly; toolbar.Update(true, config, 1); toolbar.Draw(batch, layout);
            int calls = NativeAssistantRuntime.Calls;
            toolbar.HandleInput(layout, P(layout.ModelSelector), true, 0, false, 0);
            Check(!toolbar.IsOpen && calls == NativeAssistantRuntime.Calls, "Disabled provider controls cannot open a menu or start a request.");
            return output.ToArray();
        }
    }
}
