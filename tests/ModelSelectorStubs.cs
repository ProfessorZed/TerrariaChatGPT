#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Deliberately provides no game, filesystem, credentials, HTTP or graphics APIs.
namespace Microsoft.Xna.Framework
{
    public sealed class GameTime { }
    public readonly record struct Point(int X, int Y);
    public readonly record struct Vector2(float X, float Y)
    {
        public static Vector2 Zero => new(0, 0);
        public Point ToPoint() => new((int)X, (int)Y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    }
    public readonly record struct Rectangle(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;
        public bool Contains(Point point) => point.X >= X && point.Y >= Y && point.X < Right && point.Y < Bottom;
        public static Rectangle Intersect(Rectangle a, Rectangle b)
        {
            int x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
            return new(x, y, Math.Max(0, Math.Min(a.Right, b.Right) - x), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
        }
    }
    public readonly record struct Color(int R, int G, int B)
    {
        public static Color White => new(255, 255, 255);
        public static Color Black => new(0, 0, 0);
    }
}
namespace Microsoft.Xna.Framework.Graphics
{
    public sealed class SpriteBatch
    {
        public void Draw(object texture, Microsoft.Xna.Framework.Rectangle rectangle, Microsoft.Xna.Framework.Color color) { }
    }
}
namespace Terraria.GameContent
{
    public sealed class TestAsset<T> where T : new() { public T Value { get; } = new(); }
    public sealed class TestFont
    {
        public Microsoft.Xna.Framework.Vector2 MeasureString(string text) => new(text.Length * 8, 20);
    }
    public static class FontAssets { public static TestAsset<TestFont> MouseText = new(); }
    public static class TextureAssets { public static TestAsset<object> MagicPixel = new(); }
}
namespace Terraria.UI
{
    public sealed class StyleDimension
    {
        public float Pixels;
        public void Set(float pixels, float percent) => Pixels = pixels;
    }
    public readonly record struct CalculatedStyle(Microsoft.Xna.Framework.Rectangle Rectangle)
    {
        public Microsoft.Xna.Framework.Rectangle ToRectangle() => Rectangle;
    }
    public sealed class UIMouseEvent
    {
        public Microsoft.Xna.Framework.Vector2 MousePosition { get; }
        public UIMouseEvent(int x, int y) => MousePosition = new(x, y);
    }
    public class UIElement
    {
        public UIElement? Parent;
        public bool OverflowHidden;
        public StyleDimension Height = new() { Pixels = 700 };
        public StyleDimension Width = new() { Pixels = 520 };
        public virtual void OnActivate() { }
        public virtual void OnDeactivate() { }
        public virtual void Update(Microsoft.Xna.Framework.GameTime gameTime) { }
        public virtual void LeftMouseDown(UIMouseEvent evt) { }
        protected virtual void DrawSelf(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch) { }
        public CalculatedStyle GetDimensions() => new(new(0, 0, (int)Width.Pixels, (int)Height.Pixels));
        public void Recalculate() { }
    }
    public sealed class UIState : UIElement { }
    public sealed class UserInterface { public UIState? CurrentState; }
}
namespace Terraria
{
    public sealed class Player { public bool mouseInterface; }
    public static class Main
    {
        public static bool gameMenu = true;
        public static UI.UserInterface MenuUI = new();
        public static UI.UserInterface InGameUI = new();
        public static Microsoft.Xna.Framework.Vector2 MouseScreen;
        public static Player LocalPlayer = new();
    }
    public static class Utils
    {
        public static void DrawBorderStringFourWay(Microsoft.Xna.Framework.Graphics.SpriteBatch batch,
            GameContent.TestFont font, string text, float x, float y, Microsoft.Xna.Framework.Color color,
            Microsoft.Xna.Framework.Color border, Microsoft.Xna.Framework.Vector2 origin, float scale) { }
    }
}
namespace Terraria.ModLoader
{
    public static class ModContent
    {
        public static TerrariaChatGPT.Common.Config.GptClientConfig Actual = new();
        public static T GetInstance<T>() => (T)(object)Actual;
    }
}
namespace Terraria.ModLoader.Config.UI
{
    public abstract class ConfigElement : Terraria.UI.UIElement
    {
        protected bool DrawLabel;
        protected string Label = "ChatGPT model";
        protected object Item = null!;
        private string _property = "ChatGptModel";
        public int Writes;
        public void Bind(object item, string property = "ChatGptModel") { Item = item; _property = property; }
        public virtual void OnBind() { }
        protected object GetObject() => Item.GetType().GetField(_property)!.GetValue(Item)!;
        protected void SetObject(object value)
        {
            Item.GetType().GetField(_property)!.SetValue(Item, value);
            Writes++;
        }
    }
}
namespace TerrariaChatGPT.Common.Config
{
    public enum GptProvider { ChatGPT, WikiOnly, OpenAiApi }
    public sealed class GptClientConfig
    {
        public GptProvider Provider = GptProvider.ChatGPT;
        public bool EnableOnlineAnswers = true;
        public string ChatGptModel = "";
        public string ChatGptEffort = "";
    }
}
namespace TerrariaChatGPT.Common.Services
{
    public static class GptAssistantService { public static int Epoch; }
}
namespace TerrariaChatGPT.Common.Native
{
    public static class NativeAssistantRuntime
    {
        public static int Calls;
        public static bool LastForceRefresh;
        public static Func<CancellationToken, Task<IReadOnlyList<ChatGptModelOption>>> Handler = _ => Task.FromResult<IReadOnlyList<ChatGptModelOption>>(Array.Empty<ChatGptModelOption>());
        public static Task<IReadOnlyList<ChatGptModelOption>> GetModelsAsync(CancellationToken cancellation, bool forceRefresh = false)
        {
            Interlocked.Increment(ref Calls);
            LastForceRefresh = forceRefresh;
            return Handler(cancellation);
        }
    }
}
