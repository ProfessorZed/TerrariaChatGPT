#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using ReLogic.OS;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.UI;

namespace TerrariaChatGPT.Common.Systems;

/// <summary>
/// Keeps GPT text entirely outside the vanilla outgoing-chat buffer. The original
/// chat update is never called while the GPT tab owns keyboard input.
/// </summary>
public sealed class GptChatSystem : ModSystem
{
    private const int MaximumQuestionLength = 1000;

    private static bool _openRequested;
    private static string? _requestedQuestion;
    private readonly GptChatPanel _panel = new();
    private bool _gptSelected;
    private string _question = string.Empty;
    private string _terrariaDraft = string.Empty;
    private ChatGptConnectionStatus _connection = new(false, false, string.Empty, string.Empty, false, false);
    private Task<ChatGptConnectionStatus>? _connectionRequest;
    private CancellationTokenSource _connectionCancellation = new();
    private DateTimeOffset _nextConnectionPoll;
    private bool _pollingConnection;
    private readonly QuestionRecall _recall = new();
    private int _conversationEpoch;
    private string _copyStatus = "";
    private ulong _copyStatusUntil;
    internal bool IsGptOpen => !Main.gameMenu && Main.drawingPlayerChat && _gptSelected;

    public override void Load()
    {
        if (Main.dedServ)
            return;

        On_Main.DoUpdate_HandleChat += HandleChat;
        On_Main.DrawInterface_34_PlayerChat += DrawChat;
    }

    public override void Unload()
    {
        if (!Main.dedServ)
        {
            On_Main.DoUpdate_HandleChat -= HandleChat;
            On_Main.DrawInterface_34_PlayerChat -= DrawChat;
        }

        Reset();
    }

    public override void OnWorldLoad() => Reset();

    public override void OnWorldUnload() => Reset();

    public override void PostUpdateInput()
    {
        if (_conversationEpoch != GptAssistantService.Epoch)
        {
            _conversationEpoch = GptAssistantService.Epoch;
            _recall.Clear();
        }
        bool open = !Main.dedServ && !Main.gameMenu && Main.drawingPlayerChat && _gptSelected
            && ModContent.GetInstance<GptClientConfig>().Provider == GptProvider.ChatGPT;
        if (!open)
        {
            if (_pollingConnection)
                StopConnectionPolling();
            return;
        }
        _pollingConnection = true;
        if (_connectionRequest is { IsCompleted: true })
        {
            if (_connectionRequest.IsCompletedSuccessfully)
            {
                var result = _connectionRequest.Result;
                _connection = result with { ClipboardLink = "" };
                if (result.ClipboardLink.Length != 0)
                {
                    try { Platform.Get<IClipboard>().Value = result.ClipboardLink; _copyStatus = "Link copied"; }
                    catch { _copyStatus = "Couldn't copy"; }
                    _copyStatusUntil = Main.GameUpdateCount + 180;
                }
            }
            _connectionRequest = null;
            _nextConnectionPoll = DateTimeOffset.UtcNow.AddSeconds(5);
        }
        if (_connectionRequest is null && DateTimeOffset.UtcNow >= _nextConnectionPoll)
            RequestConnection(ChatGptConnectionAction.Status);
    }

    /// <summary>
    /// Opens on the next update, after vanilla has finished processing a /gpt
    /// command and closed its own input. A supplied question becomes a draft.
    /// </summary>
    public static void OpenGpt(string? question = null)
    {
        if (Main.dedServ || Main.gameMenu)
            return;

        _requestedQuestion = question;
        _openRequested = true;
    }

    private void Reset()
    {
        _openRequested = false;
        _requestedQuestion = null;
        _gptSelected = false;
        _question = string.Empty;
        _terrariaDraft = string.Empty;
        _recall.Clear();
        _copyStatus = "";
        _panel.Reset();
        StopConnectionPolling();
        _connection = new(false, false, string.Empty, string.Empty, false, false);
    }

    private void HandleChat(On_Main.orig_DoUpdate_HandleChat orig)
    {
        if (Main.gameMenu)
        {
            orig();
            return;
        }

        if (_openRequested)
        {
            _openRequested = false;
            SelectGpt();
            if (_requestedQuestion is not null)
            {
                _question = LimitQuestion(_requestedQuestion);
                _recall.Edited();
            }
            _requestedQuestion = null;
            Main.OpenPlayerChat();
        }

        // Let signs, chest naming and other mods retain their own input focus.
        if (!Main.drawingPlayerChat || Main.editSign || Main.editChest ||
            Main.CurrentInputTextTakerOverride is not null)
        {
            orig();
            return;
        }

        GptChatLayout layout = GptChatPanel.GetLayout();
        Point mouse = GetUiMouse();
        bool clicked = Main.mouseLeft && Main.mouseLeftRelease;
        bool switched = false;

        if (clicked && layout.TerrariaTab.Contains(mouse))
        {
            SelectTerraria();
            switched = true;
        }
        else if (clicked && layout.GptTab.Contains(mouse))
        {
            SelectGpt();
            switched = true;
        }
        else if (JustPressed(Keys.Tab))
        {
            if (_gptSelected)
                SelectTerraria();
            else
                SelectGpt();
            switched = true;
        }

        if (layout.TerrariaTab.Contains(mouse) || layout.GptTab.Contains(mouse))
            Main.LocalPlayer.mouseInterface = true;

        if (!_gptSelected)
        {
            // Avoid treating the same Enter/Tab event as a public-chat send on
            // the exact frame the user changes channels.
            if (!switched)
                orig();
            return;
        }

        PlayerInput.WritingText = true;
        Main.LocalPlayer.mouseInterface = true;
        Main.chatText = string.Empty;

        if (JustPressed(Keys.Escape))
        {
            Main.ClosePlayerChat();
            Main.clrInput();
            return;
        }

        if (layout.Panel.Contains(mouse))
        {
            // The hotbar reads a separate wheel delta. Clearing the UI delta
            // alone does not consume it; reserve scrolling for this panel even
            // when its history is already at the top or bottom.
            PlayerInput.LockVanillaMouseScroll("TerrariaChatGPT:Chat");
            if (PlayerInput.ScrollWheelDeltaForUI != 0)
            {
                _panel.Scroll(PlayerInput.ScrollWheelDeltaForUI > 0 ? 3 : -3);
                PlayerInput.ScrollWheelDeltaForUI = 0;
            }
        }

        if (JustPressed(Keys.PageUp))
            _panel.Scroll(10);
        if (JustPressed(Keys.PageDown))
            _panel.Scroll(-10);
        if (JustPressed(Keys.End))
            _panel.ScrollToLatest();

        if (ModContent.GetInstance<GptClientConfig>().Provider == GptProvider.ChatGPT && layout.ManageUsage.Contains(mouse))
        {
            Main.LocalPlayer.mouseInterface = true;
            if (clicked)
            {
                RequestConnection(ChatGptConnectionAction.CopyUsageLink);
                Main.clrInput();
                return;
            }
        }

        if (layout.CopyAnswer.Contains(mouse))
        {
            Main.LocalPlayer.mouseInterface = true;
            if (clicked)
            {
                string? answer = GptAssistantService.LastAnswer;
                try
                {
                    if (!string.IsNullOrWhiteSpace(answer))
                    {
                        Platform.Get<IClipboard>().Value = answer;
                        _copyStatus = "Copied";
                    }
                    else _copyStatus = "No answer yet";
                }
                catch { _copyStatus = "Couldn't copy"; }
                _copyStatusUntil = Main.GameUpdateCount + 150;
                Main.clrInput();
                return;
            }
        }

        if (clicked && _panel.HandleClick(mouse))
        {
            Main.clrInput();
            return;
        }

        if (switched)
            return;

        if (JustPressed(Keys.Up) || JustPressed(Keys.Down))
        {
            _question = JustPressed(Keys.Up) ? _recall.Previous(_question) : _recall.Next(_question);
            Main.clrInput();
            return;
        }

        string nextQuestion = LimitQuestion(Main.GetInputText(_question));
        if (nextQuestion != _question) _recall.Edited();
        _question = nextQuestion;
        if (Main.inputTextEscape)
        {
            Main.ClosePlayerChat();
            return;
        }

        // Toggle-chat runs after this hook and restores chatRelease on key-up.
        // The explicit edge also prevents a held opening Enter from submitting.
        if (Main.inputTextEnter && Main.chatRelease && JustPressed(Keys.Enter))
        {
            Main.chatRelease = false;
            if (!GptAssistantService.IsBusy && !string.IsNullOrWhiteSpace(_question))
            {
                string question = _question.Trim();
                _recall.Record(question);
                _question = string.Empty;
                _panel.ScrollToLatest();
                GptAssistantService.Submit(question);
            }
            Main.clrInput();
        }
    }

    private void DrawChat(On_Main.orig_DrawInterface_34_PlayerChat orig, Main self)
    {
        if (Main.gameMenu || !Main.drawingPlayerChat || !_gptSelected)
        {
            orig(self);
            if (!Main.gameMenu && Main.drawingPlayerChat)
                _panel.DrawTabs(Main.spriteBatch, GptChatPanel.GetLayout(), false);
            return;
        }

        PlayerInput.WritingText = true;
        self.HandleIME();
        _panel.Draw(Main.spriteBatch, _question, GptAssistantService.Messages,
            GptAssistantService.IsBusy, ModContent.GetInstance<GptClientConfig>().Provider,
            _connection, _connectionRequest is not null,
            Main.GameUpdateCount < _copyStatusUntil ? _copyStatus : "");
    }

    private void SelectGpt()
    {
        if (!_gptSelected)
        {
            _terrariaDraft = Main.chatText;
            Main.chatText = string.Empty;
            _gptSelected = true;
        }
        Main.clrInput();
    }

    private void SelectTerraria()
    {
        if (_gptSelected)
        {
            _gptSelected = false;
            Main.chatText = _terrariaDraft;
        }
        Main.clrInput();
    }

    private void RequestConnection(ChatGptConnectionAction action)
    {
        // A deliberate usage-settings click takes priority over a status poll.
        // It never passes through Terraria's public outgoing-chat handler.
        if (_connectionRequest is not null)
        {
            if (action == ChatGptConnectionAction.Status)
                return;
            StopConnectionPolling();
            _pollingConnection = true;
        }
        _connectionRequest = ChatGptConnectionClient.RequestAsync(action, _connectionCancellation.Token);
    }

    private void StopConnectionPolling()
    {
        _connectionCancellation.Cancel();
        _connectionCancellation.Dispose();
        _connectionCancellation = new CancellationTokenSource();
        _connectionRequest = null;
        _nextConnectionPoll = DateTimeOffset.MinValue;
        _pollingConnection = false;
    }

    private static bool JustPressed(Keys key) =>
        Main.keyState.IsKeyDown(key) && Main.oldKeyState.IsKeyUp(key);

    private static Point GetUiMouse()
    {
        // Main.mouseX/Y are in world zoom coordinates during HandleChat.
        // Mouse.GetState gives screen pixels, independent of the world camera.
        MouseState mouse = Mouse.GetState();
        float scale = Main.UIScale;
        return new Point((int)(mouse.X / scale), (int)(mouse.Y / scale));
    }

    private static string LimitQuestion(string text) =>
        text.Length <= MaximumQuestionLength ? text : text[..MaximumQuestionLength];
}
