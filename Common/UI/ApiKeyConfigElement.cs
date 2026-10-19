#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.OS;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.ModLoader.Config.UI;
using Terraria.UI;
using TerrariaGPT.Common.Security;

namespace TerrariaGPT.Common.UI;

/// <summary>
/// A write-only, masked credential editor. No key is assigned to the config
/// object, rendered as plaintext, or read back from encrypted storage.
/// </summary>
public sealed class ApiKeyConfigElement : ConfigElement
{
    private static ApiKeyConfigElement? _draftOwner;
    private string _draft = string.Empty;
    private string _feedback = string.Empty;
    private bool _feedbackError;
    private bool _focused;
    private bool _wasBlockingInput;
    private bool _saved;

    public override void OnBind()
    {
        base.OnBind();
        DrawLabel = false;
        Height.Set(154f, 0f);
        _saved = GptCredentialStore.HasSavedKey;
    }

    public override void OnDeactivate()
    {
        DiscardDraft();
        base.OnDeactivate();
    }

    public override void LeftMouseDown(UIMouseEvent evt)
    {
        base.LeftMouseDown(evt);
        Point mouse = evt.MousePosition.ToPoint();
        Rectangle field = InputRectangle();
        if (field.Contains(mouse))
        {
            Focus();
            return;
        }

        Rectangle[] buttons = ButtonRectangles();
        if (buttons[0].Contains(mouse))
        {
            Paste();
            return;
        }
        if (buttons[1].Contains(mouse))
        {
            Save();
            return;
        }
        if (buttons[2].Contains(mouse))
        {
            Clear();
            return;
        }
        Blur();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (!_focused)
            return;

        if (Main.mouseLeft && Main.mouseLeftRelease && !ContainsPoint(Main.MouseScreen))
        {
            Blur();
            return;
        }

        BlockGameInput();
        bool control = Main.keyState.IsKeyDown(Keys.LeftControl) || Main.keyState.IsKeyDown(Keys.RightControl);
        if (control && (Main.keyState.IsKeyDown(Keys.C) || Main.keyState.IsKeyDown(Keys.X)))
        {
            // Masked credentials cannot be copied back onto the clipboard.
            Main.clrInput();
            return;
        }

        string next = Main.GetInputText(_draft);
        if (next.Length > GptCredentialStore.MaximumKeyLength)
        {
            SetFeedback("InvalidKey", true);
            // Keep an over-length marker so Save can never accept a truncated key.
            _draft = next[..(GptCredentialStore.MaximumKeyLength + 1)];
        }
        else if (next != _draft)
        {
            _draft = next;
            _feedback = string.Empty;
        }

        if (Main.inputTextEscape)
            DiscardDraft();
        else if (Main.inputTextEnter)
            Save();
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);
        Rectangle area = GetDimensions().ToRectangle();
        DrawText(spriteBatch, Label, new Vector2(area.X + 10, area.Y + 7), Color.White, 0.8f, area.Width - 20);

        Rectangle input = InputRectangle();
        DrawBox(spriteBatch, input, new Color(18, 25, 40),
            _focused ? new Color(124, 227, 187) : new Color(106, 125, 157));
        string display = _draft.Length == 0
            ? Text("InputPlaceholder")
            : new string('*', Math.Min(36, _draft.Length));
        if (_focused && Main.GameUpdateCount % 60 < 30)
            display += "|";
        DrawText(spriteBatch, display, new Vector2(input.X + 9, input.Y + 7),
            _draft.Length == 0 ? new Color(175, 186, 205) : Color.White, 0.75f, input.Width - 18);

        Rectangle[] buttons = ButtonRectangles();
        string[] labels = { Text("Paste"), Text("Save"), Text("Clear") };
        for (int i = 0; i < buttons.Length; i++)
        {
            bool hovered = buttons[i].Contains(Main.MouseScreen.ToPoint());
            DrawBox(spriteBatch, buttons[i], hovered ? new Color(69, 101, 135) : new Color(40, 64, 98),
                new Color(117, 147, 180));
            DrawText(spriteBatch, labels[i], new Vector2(buttons[i].X + 9, buttons[i].Y + 6),
                Color.White, 0.7f, buttons[i].Width - 18);
        }

        string feedback = _feedback.Length > 0 ? _feedback : Text(_saved ? "Stored" : "NotStored");
        Color statusColor = _feedbackError ? new Color(255, 183, 165) : new Color(181, 235, 203);
        DrawText(spriteBatch, feedback, new Vector2(area.X + 10, area.Y + 111), statusColor, 0.63f, area.Width - 20);
        DrawText(spriteBatch, Text("UsageNotice"), new Vector2(area.X + 10, area.Y + 132),
            new Color(192, 201, 216), 0.58f, area.Width - 20);
    }

    public static void MaintainInput()
    {
        if (_draftOwner is null)
            return;
        UIElement root = _draftOwner;
        while (root.Parent is not null)
            root = root.Parent;
        UIState? active = Main.gameMenu ? Main.MenuUI.CurrentState : Main.InGameUI.CurrentState;
        if (!ReferenceEquals(root, active))
            _draftOwner.DiscardDraft();
        else if (_draftOwner._focused)
            _draftOwner.BlockGameInput();
    }

    public static void DiscardActiveDraft() => _draftOwner?.DiscardDraft();

    private void Focus()
    {
        if (_draftOwner != this)
            DiscardActiveDraft();
        _draftOwner = this;
        if (!_focused)
        {
            _wasBlockingInput = Main.blockInput;
            _focused = true;
            Main.clrInput();
        }
        BlockGameInput();
    }

    private void Blur()
    {
        if (_focused)
        {
            _focused = false;
            if (ReferenceEquals(Main.CurrentInputTextTakerOverride, this))
                Main.CurrentInputTextTakerOverride = null;
            Main.blockInput = _wasBlockingInput;
            Main.clrInput();
        }
    }

    private void DiscardDraft()
    {
        Blur();
        _draft = string.Empty;
        if (_draftOwner == this)
            _draftOwner = null;
    }

    private void BlockGameInput()
    {
        Main.blockInput = true;
        Main.CurrentInputTextTakerOverride = this;
        PlayerInput.WritingText = true;
        if (!Main.gameMenu)
            Main.LocalPlayer.mouseInterface = true;
    }

    private void Paste()
    {
        Focus();
        try
        {
            string pasted = Platform.Get<IClipboard>().Value.Trim();
            if (!GptCredentialStore.IsValidKey(pasted))
            {
                SetFeedback("InvalidKey", true);
                return;
            }
            _draft = pasted;
            _feedback = string.Empty;
            Main.clrInput();
        }
        catch
        {
            // Clipboard errors must never echo clipboard text.
            SetFeedback("PasteFailed", true);
        }
    }

    private void Save()
    {
        try
        {
            if (!GptCredentialStore.IsValidKey(_draft.Trim()))
            {
                SetFeedback("InvalidKey", true);
                return;
            }
            GptCredentialStore.SaveKey(_draft);
            _saved = true;
            DiscardDraft();
            SetFeedback("Saved", false);
        }
        catch
        {
            // Errors are intentionally generic and never include a key.
            SetFeedback("SaveFailed", true);
        }
    }

    private void Clear()
    {
        try
        {
            GptCredentialStore.ClearKey();
            _saved = false;
            DiscardDraft();
            SetFeedback("Cleared", false);
        }
        catch
        {
            SetFeedback("SaveFailed", true);
        }
    }

    private void SetFeedback(string key, bool error)
    {
        _feedback = Text(key);
        _feedbackError = error;
    }

    private static string Text(string key) => Language.GetTextValue($"Mods.TerrariaChatGPT.CredentialEditor.{key}");

    private Rectangle InputRectangle()
    {
        Rectangle area = GetDimensions().ToRectangle();
        return new Rectangle(area.X + 8, area.Y + 32, Math.Max(40, area.Width - 16), 32);
    }

    private Rectangle[] ButtonRectangles()
    {
        Rectangle area = GetDimensions().ToRectangle();
        int width = Math.Max(35, (area.Width - 28) / 3);
        return new[] {
            new Rectangle(area.X + 8, area.Y + 72, width, 30),
            new Rectangle(area.X + 14 + width, area.Y + 72, width, 30),
            new Rectangle(area.X + 20 + width * 2, area.Y + 72, width, 30)
        };
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
}
