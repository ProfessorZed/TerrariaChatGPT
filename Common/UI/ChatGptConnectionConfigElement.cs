#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config.UI;
using Terraria.UI;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Common.UI;

/// <summary>Browser-based ChatGPT sign-in. The game receives account labels, never OAuth tokens.</summary>
public sealed class ChatGptConnectionConfigElement : ConfigElement
{
    private static readonly List<ChatGptConnectionConfigElement> Editors = new();
    private ChatGptConnectionStatus _status = new(false, false, string.Empty, string.Empty, false, false);
    private CancellationTokenSource _cancellation = new();
    private Task<ChatGptConnectionStatus>? _pending;
    private DateTimeOffset _nextPoll;
    private WelcomeOverlay? _welcome;
    private bool _registered;

    public override void OnBind()
    {
        base.OnBind();
        DrawLabel = false;
        Height.Set(168f, 0f);
        Register();
    }

    public override void OnActivate()
    {
        if (_cancellation.IsCancellationRequested)
        {
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();
        }
        _nextPoll = DateTimeOffset.MinValue;
        Register();
        base.OnActivate();
    }

    public override void OnDeactivate()
    {
        _cancellation.Cancel();
        _pending = null;
        base.OnDeactivate();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (!IsShown())
            return;
        if (_pending is { IsCompleted: true })
        {
            if (_pending.IsCompletedSuccessfully)
                _status = _pending.Result;
            _pending = null;
            _nextPoll = DateTimeOffset.UtcNow.AddSeconds(_status.SigningIn ? 1 : 4);
        }
        if (_pending is null && DateTimeOffset.UtcNow >= _nextPoll)
            Request(ChatGptConnectionAction.Status);
        if (!Main.gameMenu && ContainsPoint(Main.MouseScreen))
            Main.LocalPlayer.mouseInterface = true;
    }

    public override void LeftMouseDown(UIMouseEvent evt)
    {
        base.LeftMouseDown(evt);
        if (_pending is not null || _welcome is not null)
            return;
        Point mouse = evt.MousePosition.ToPoint();
        Rectangle[] buttons = ButtonRectangles();
        if (buttons[0].Contains(mouse))
        {
            if (_status.Connected)
                Request(ChatGptConnectionAction.SignOut);
            else if (!_status.SigningIn)
                Request(ChatGptConnectionAction.SignIn);
        }
        else if (buttons[1].Contains(mouse))
            Request(ChatGptConnectionAction.ManageUsage);
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);
        Rectangle area = GetDimensions().ToRectangle();
        DrawText(spriteBatch, Text("Title"), new Vector2(area.X + 10, area.Y + 8), Color.White, 0.8f, area.Width - 20);
        string connection = _status.Connected
            ? Text(_status.UsingPlan ? "UsingPlan" : "Connected") + (_status.Account.Length > 0 ? " · " + _status.Account : string.Empty)
            : _status.SigningIn ? Text("SigningIn") : Text("NotConnected");
        DrawText(spriteBatch, connection, new Vector2(area.X + 10, area.Y + 34),
            _status.Connected ? new Color(181, 235, 203) : new Color(207, 215, 228), 0.66f, area.Width - 20);
        DrawText(spriteBatch, Text("PlanNotice"), new Vector2(area.X + 10, area.Y + 56), new Color(192, 201, 216), 0.6f, area.Width - 20);

        Rectangle[] buttons = ButtonRectangles();
        DrawButton(spriteBatch, buttons[0], _status.Connected ? Text("SignOut") : Text("Continue"),
            _pending is null && !_status.SigningIn, true);
        DrawButton(spriteBatch, buttons[1], Text("ManageUsage"), _pending is null, false);
        string message = _status.Message.Length > 0 ? _status.Message : Text(_status.Connected ? "Ready" : "BrowserNotice");
        DrawText(spriteBatch, message, new Vector2(area.X + 10, area.Y + 123),
            _status.IsError ? new Color(255, 183, 165) : new Color(193, 209, 221), 0.58f, area.Width - 20);
        DrawText(spriteBatch, Text("NoFallback"), new Vector2(area.X + 10, area.Y + 145), new Color(192, 201, 216), 0.57f, area.Width - 20);
    }

    private void Request(ChatGptConnectionAction action)
    {
        if (_pending is not null || _cancellation.IsCancellationRequested)
            return;
        _pending = ChatGptConnectionClient.RequestAsync(action, _cancellation.Token);
    }

    private void Register()
    {
        if (_registered)
            return;
        Editors.Add(this);
        _registered = true;
    }

    // Called before UI updates. Adding/removing a root child from inside an
    // editor's Update would invalidate the root's child enumeration.
    public static void MaintainOverlays()
    {
        for (int i = Editors.Count - 1; i >= 0; i--)
        {
            ChatGptConnectionConfigElement editor = Editors[i];
            bool shouldShow = !editor._cancellation.IsCancellationRequested && editor.IsShown()
                && editor._status.Connected && editor._status.UsingPlan && editor._status.WelcomePending;
            if (shouldShow && editor._welcome is null)
            {
                ApiKeyConfigElement.DiscardActiveDraft();
                editor._welcome = new WelcomeOverlay(editor);
                editor.GetRoot().Append(editor._welcome);
            }
            else if (!shouldShow)
                editor.RemoveWelcome();
            if (editor._cancellation.IsCancellationRequested)
            {
                Editors.RemoveAt(i);
                editor._registered = false;
            }
        }
    }

    public static void ClearOverlays()
    {
        foreach (ChatGptConnectionConfigElement editor in Editors)
        {
            editor._cancellation.Cancel();
            editor.RemoveWelcome();
            editor._registered = false;
        }
        Editors.Clear();
    }

    private bool IsShown()
    {
        UIElement root = GetRoot();
        UIState? active = Main.gameMenu ? Main.MenuUI.CurrentState : Main.InGameUI.CurrentState;
        if (!ReferenceEquals(root, active))
            return false;
        Rectangle rectangle = GetDimensions().ToRectangle();
        for (UIElement? parent = Parent; parent is not null; parent = parent.Parent)
            if (parent.OverflowHidden && !rectangle.Intersects(parent.GetDimensions().ToRectangle()))
                return false;
        return true;
    }

    private UIElement GetRoot()
    {
        UIElement root = this;
        while (root.Parent is not null)
            root = root.Parent;
        return root;
    }

    private void RemoveWelcome()
    {
        _welcome?.Remove();
        _welcome = null;
    }

    private Rectangle[] ButtonRectangles()
    {
        Rectangle area = GetDimensions().ToRectangle();
        int available = Math.Max(60, area.Width - 26);
        int firstWidth = available * 3 / 5;
        return new[] {
            new Rectangle(area.X + 8, area.Y + 80, firstWidth, 32),
            new Rectangle(area.X + 18 + firstWidth, area.Y + 80, available - firstWidth, 32)
        };
    }

    private static string Text(string key) => Language.GetTextValue($"Mods.TerrariaChatGPT.ChatGptConnection.{key}");

    private static void DrawButton(SpriteBatch spriteBatch, Rectangle rectangle, string text, bool enabled, bool primary)
    {
        bool hovered = enabled && rectangle.Contains(Main.MouseScreen.ToPoint());
        Color fill = !enabled ? new Color(43, 48, 59) : primary
            ? hovered ? new Color(244, 247, 250) : new Color(228, 232, 237)
            : hovered ? new Color(69, 101, 135) : new Color(40, 64, 98);
        DrawBox(spriteBatch, rectangle, fill, new Color(117, 147, 180));
        DrawText(spriteBatch, text, new Vector2(rectangle.X + 10, rectangle.Y + 7),
            !enabled ? new Color(157, 166, 180) : primary ? new Color(24, 29, 38) : Color.White, 0.69f, rectangle.Width - 20);
    }

    private static void DrawBox(SpriteBatch spriteBatch, Rectangle rectangle, Color fill, Color border)
    {
        spriteBatch.Draw(TextureAssets.MagicPixel.Value, rectangle, border);
        spriteBatch.Draw(TextureAssets.MagicPixel.Value,
            new Rectangle(rectangle.X + 1, rectangle.Y + 1, rectangle.Width - 2, rectangle.Height - 2), fill);
    }

    private static void DrawText(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale, int width)
    {
        float measured = FontAssets.MouseText.Value.MeasureString(text).X * scale;
        if (measured > width && measured > 0)
            scale *= width / measured;
        Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.MouseText.Value, text,
            position.X, position.Y, color, Color.Black, Vector2.Zero, scale);
    }

    private sealed class WelcomeOverlay : UIElement
    {
        private readonly ChatGptConnectionConfigElement _owner;

        public WelcomeOverlay(ChatGptConnectionConfigElement owner)
        {
            _owner = owner;
            Width.Set(0, 1f);
            Height.Set(0, 1f);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!Main.gameMenu)
                Main.LocalPlayer.mouseInterface = true;
        }

        public override void LeftMouseDown(UIMouseEvent evt)
        {
            // The full-size element intercepts clicks so settings below cannot
            // change while the first-use plan confirmation is open.
            Rectangle panel = Panel();
            if (_owner._pending is null && GotIt(panel).Contains(evt.MousePosition.ToPoint()))
                _owner.Request(ChatGptConnectionAction.Welcome);
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            Rectangle area = GetDimensions().ToRectangle();
            spriteBatch.Draw(TextureAssets.MagicPixel.Value, area, new Color(0, 0, 0, 180));
            Rectangle panel = Panel();
            DrawBox(spriteBatch, panel, new Color(24, 35, 50), new Color(126, 221, 180));
            DrawText(spriteBatch, Text("WelcomeTitle"), new Vector2(panel.X + 20, panel.Y + 20), Color.White, 0.8f, panel.Width - 40);
            DrawText(spriteBatch, Text("WelcomeBody"), new Vector2(panel.X + 20, panel.Y + 59), new Color(210, 222, 234), 0.63f, panel.Width - 40);
            DrawText(spriteBatch, Text("WelcomeUsage"), new Vector2(panel.X + 20, panel.Y + 84), new Color(210, 222, 234), 0.63f, panel.Width - 40);
            DrawButton(spriteBatch, GotIt(panel), Text("GotIt"), _owner._pending is null, true);
        }

        private Rectangle Panel()
        {
            Rectangle area = GetDimensions().ToRectangle();
            int width = Math.Min(640, Math.Max(220, area.Width - 40));
            return new Rectangle(area.Center.X - width / 2, area.Center.Y - 91, width, 182);
        }

        private static Rectangle GotIt(Rectangle panel) => new(panel.X + 20, panel.Bottom - 53, panel.Width - 40, 33);
    }
}
