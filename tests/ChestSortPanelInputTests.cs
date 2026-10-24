#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria.GameInput;
using TerrariaChatGPT.Common.Sorting;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.UI;

namespace Microsoft.Xna.Framework
{
    public readonly record struct Point(int X, int Y);
    public readonly record struct Vector2(float X, float Y);
    public readonly record struct Rectangle(int X, int Y, int Width, int Height)
    { public int Right => X + Width; public int Bottom => Y + Height; }
    public readonly record struct Color(int R, int G, int B, int A = 255);
}
namespace Microsoft.Xna.Framework.Graphics
{ public sealed class SpriteBatch { public void Draw(object texture, Rectangle rectangle, Color color) { } } }
namespace Microsoft.Xna.Framework.Input
{
    public enum ButtonState { Released, Pressed }
    public readonly record struct MouseState(ButtonState LeftButton, ButtonState RightButton);
    public static class Mouse
    { public static MouseState State; public static MouseState GetState() => State; }
}
namespace Terraria
{
    public sealed class Player { public bool mouseInterface, controlUseItem = true, controlUseTile = true; }
    public static class Main
    {
        public static bool dedServ, gameMenu, mouseLeft, mouseLeftRelease = true, mouseRight, mouseRightRelease = true;
        public static float UIScale = 1;
        public static int worldID = 1;
        public static Player LocalPlayer = new();
    }
}
namespace Terraria.GameInput
{
    public static class PlayerInput
    {
        public static int RealScreenWidth = 1280, RealScreenHeight = 720, ScrollWheelDeltaForUI, ScrollLocks;
        public static bool WritingText;
        public static void LockVanillaMouseScroll(string owner) => ScrollLocks++;
    }
}
namespace Terraria.GameContent
{
    public sealed class Font { public int LineSpacing = 24; public float CharacterSpacing; }
    public sealed class Asset<T> where T : new() { public T Value = new(); }
    public static class FontAssets { public static Asset<Font> MouseText = new(); }
    public static class TextureAssets { public static Asset<object> MagicPixel = new(); }
}
namespace Terraria.ModLoader
{ public static class ModContent { public static T GetInstance<T>() where T : new() => Holder<T>.Value; private static class Holder<T> where T : new() { internal static T Value = new(); } } }
namespace TerrariaChatGPT.Common.Config
{ public sealed class GptClientConfig { public float ChatTextScale = 0.9f; } }
namespace TerrariaChatGPT.Common.UI
{
    internal static class GptChatPanel
    {
        public static readonly Dictionary<char, ChatGlyphMetrics> GlyphMetrics = new();
        public static readonly List<object> Operations = new();
        public static readonly List<(string Text, Rectangle Bounds, bool Enabled)> Buttons = new();
        public static readonly List<string> Text = new();
        public static ChatGlyphMetrics Metrics(Terraria.GameContent.Font font, char c) => GlyphMetrics.TryGetValue(c,out var metrics) ? metrics : new(0,8,0);
        public static void DrawButton(SpriteBatch batch, Rectangle r, string text, bool enabled, float scale)
        {
            Buttons.Add((text,r,enabled));
            DrawBox(batch,r,new(29,45,62),new(73,106,124));
            string visible=HeadToFit(text,r.Width-10,scale);
            DrawClippedText(batch,visible,new(r.X+(r.Width-Measure(visible)*scale)/2,r.Y+3),enabled?new(213,234,229):new(123,142,157),scale,new(r.X+3,r.Y+1,Math.Max(0,r.Width-6),Math.Max(0,r.Height-2)));
        }
        public static void DrawBox(SpriteBatch batch, Rectangle r, Color fill, Color border) => Operations.Add(new { kind="box",bounds=r,fill,border });
        public static void DrawClippedText(SpriteBatch batch, string text, Vector2 point, Color color, float scale, Rectangle clip)
        { Text.Add(text); Operations.Add(new { kind="text",text,point,color,scale,clip }); }
        private static float Measure(string text)
        {
            float pen=0; bool first=true; var font=Terraria.GameContent.FontAssets.MouseText.Value;
            foreach(char c in text) { var m=Metrics(font,c); pen+=first?Math.Max(m.Left,0):font.CharacterSpacing+m.Left; pen+=m.Width+m.Right; first=false; }
            return pen;
        }
        public static string HeadToFit(string text, int width, float scale)
        {
            if(width<=0)return "";
            if(Measure(text)*scale<=width)return text;
            while(text.Length>0 && Measure(text+"...")*scale>width)text=text[..^1];
            return text+"...";
        }
    }
}
namespace TerrariaChatGPT.Common.Sorting
{
    public sealed record ChestSortPreview(ChestSortPlan Plan, int EligibleChestCount = 1);
    public sealed record ChestSortResult(bool Success, string Message, int MovedItemCount, int MovedStackCount);
    public static class ChestSortService
    {
        public static int Previews, Confirms, Cancels;
        public static ChestSortPreview? Preview;
        public static string Status = "Multiplayer sorting is not available.";
        public static bool Success = true;
        public static SortSuggestion? ReceivedSuggestion;
        public static ChestSortPreview? BeginPreview(SortSuggestion? suggestion = null) { Previews++; ReceivedSuggestion = suggestion; return Preview; }
        public static ChestSortResult Confirm() { Confirms++; return new(Success, Success ? "Moved safely." : "Chest changed. Refresh preview.",12,1); }
        public static void Cancel() => Cancels++;
    }
}
namespace TerrariaChatGPT.Common.Services
{
    public static class GptAssistantService
    {
        public static int Epoch, RememberCalls;
        public static SortSuggestion? LatestSortSuggestion;
        public static bool IsCurrentSortSuggestion(SortSuggestion suggestion) => ReferenceEquals(suggestion, LatestSortSuggestion);
        public static void CompleteSortSuggestion(SortSuggestion suggestion)
        { if (ReferenceEquals(suggestion, LatestSortSuggestion)) LatestSortSuggestion=null; }
        public static bool RememberSortChoices(SortSuggestion suggestion, int epoch, out string message)
        { RememberCalls++; message = "Saved. Undo in chat."; return true; }
    }
}
namespace TerrariaChatGPT.Tests
{
    public static class ChestSortPanelInputTests
    {
        public static string[] Run()
        {
            var passed = new List<string>();
            void Check(bool ok, string message) { if (!ok) throw new Exception(message); passed.Add(message); }
            static Point At(ChatRect rect) => new(rect.X+2,rect.Y+2);
            static void Physical(bool left, bool right = false)
            {
                Mouse.State = new(left ? ButtonState.Pressed : ButtonState.Released,right ? ButtonState.Pressed : ButtonState.Released);
                Terraria.Main.mouseLeft = left; Terraria.Main.mouseRight = right;
                Terraria.Main.mouseLeftRelease = true; Terraria.Main.mouseRightRelease = true;
            }
            var preview = new ChestSortPreview(new ChestSortPlan
            {
                Moves = new[] { new ChestSortMove { SourceSlot=10,ChestId=1,ItemName="Wood",ChestName="Building",Amount=12 } },
                Unsorted = new[] { new ChestSortUnsortedItem { Slot=11,Name="Gel",Remaining=15,SuggestedChest="Materials",Reason="No matching named chest." } }
            });
            ChestSortService.Preview = preview;
            ChestSortService.Previews = ChestSortService.Confirms = 0;
            var panel = new ChestSortPanel(); var batch = new SpriteBatch();
            var layout = ChestSortPanelGeometry.Calculate(1280,720);
            void Input(bool left, Point point, bool escape = false, int page = 0, bool right = false)
            { Physical(left,right); panel.HandleInput(point,escape,page); }
            void Click(ChatRect rect) { Input(false,At(rect)); Input(true,At(rect)); }
            Physical(true); panel.Open(); panel.Draw(batch);
            Input(true,At(layout.Confirm)); Input(true,At(layout.Confirm));
            Check(ChestSortService.Previews==1 && ChestSortService.Confirms==0,"Opening, drawing and holding the opening click never move items.");
            Input(false,At(layout.Confirm),right:true);
            Check(ChestSortService.Confirms==0 && !Terraria.Main.mouseRight && !Terraria.Main.mouseRightRelease,
                "Right-click does not confirm or leak to inventory stack splitting.");
            Input(false,At(layout.Confirm));
            Check(ChestSortService.Confirms==0,"Hovering Confirm has no action.");
            PlayerInput.ScrollWheelDeltaForUI=-120; int locks=PlayerInput.ScrollLocks;
            Input(false,new Point(0,0));
            Check(PlayerInput.ScrollWheelDeltaForUI==0 && PlayerInput.ScrollLocks>locks,"Modal consumes wheel input even outside the panel, reserving the hotbar.");
            Input(false,new Point(0,0),page:1);
            Check(ChestSortService.Confirms==0,"Keyboard paging can only navigate the preview.");
            Click(layout.Confirm); panel.Draw(batch);
            Check(ChestSortService.Confirms==1 && !GptChatPanel.Buttons.Last(b=>b.Text=="Confirm sort").Enabled,
                "Only a fresh Confirm click executes once and immediately disables confirmation.");
            Input(true,At(layout.Confirm)); Click(layout.Confirm);
            Check(ChestSortService.Confirms==1,"Held and repeated clicks cannot replay an executed plan.");
            Check(string.Join(" ",GptChatPanel.Text).Contains("Materials"),"Unsorted-item chest recommendations stay visible after sorting.");
            Click(layout.Refresh); panel.Draw(batch);
            Check(ChestSortService.Previews==2 && ChestSortService.Confirms==1,"Refresh rebuilds only a preview, without executing it.");
            ChestSortService.Success=false; Click(layout.Confirm);
            Click(layout.Confirm);
            Check(ChestSortService.Confirms==2 && ChestSortService.Previews==2,"Stale confirmation failure cannot silently rebuild or execute again.");
            Click(layout.Refresh); panel.Draw(batch);
            int before=ChestSortService.Confirms;
            Input(false,new Point(0,0));
            PlayerInput.RealScreenWidth=640; PlayerInput.RealScreenHeight=480;
            var small = ChestSortPanelGeometry.Calculate(640,480);
            Input(true,At(small.Confirm));
            Check(ChestSortService.Confirms==before,"A resize cannot redirect a stale click onto Confirm.");
            panel.Draw(batch);
            Input(true,At(small.Cancel),escape:true);
            Check(!panel.IsOpen && panel.OwnsPointer && !Terraria.Main.mouseLeft,"Escape closes preview while preserving held gesture ownership.");
            Physical(true); panel.Update(false);
            Check(panel.OwnsPointer && !Terraria.Main.mouseLeft,"A closed modal keeps masking its held gesture across frames.");
            Physical(false); panel.Update(false);
            Check(!panel.OwnsPointer,"Physical release restores normal inventory input.");
            ChestSortService.Preview=null; Physical(false); panel.Open(); panel.Draw(batch);
            Input(false,At(small.Confirm)); Input(true,At(small.Confirm));
            Check(ChestSortService.Confirms==before && !GptChatPanel.Buttons.Last(b=>b.Text=="Confirm sort").Enabled,
                "An unavailable/empty preview cannot confirm.");
            Physical(false); panel.Update(false);
            Check(!panel.IsOpen,"Closing GPT or leaving the world discards the modal preview.");

            PlayerInput.RealScreenWidth=1280; PlayerInput.RealScreenHeight=720;
            var suggestion = new SortSuggestion(Array.AsReadOnly(new[] { new SortAssignment("Terraria/AmethystHook", "Tools & Travel") }));
            GptAssistantService.LatestSortSuggestion = suggestion;
            ChestSortService.Preview = preview;
            ChestSortService.Success = true;
            Physical(false); panel.Open(suggestion); panel.Draw(batch);
            Check(ReferenceEquals(ChestSortService.ReceivedSuggestion,suggestion),"Review sort passes the accepted GPT mapping into the real preview path.");
            Click(layout.Refresh); panel.Draw(batch);
            Check(ReferenceEquals(ChestSortService.ReceivedSuggestion,suggestion),"Refreshing retains the exact proposed destination instead of reverting to generic sorting.");
            before=ChestSortService.Confirms;
            int previewsBefore = ChestSortService.Previews;
            Click(layout.Remember); panel.Draw(batch);
            Check(GptAssistantService.RememberCalls==1 && ChestSortService.Confirms==before && ChestSortService.Previews==previewsBefore+1,
                "Remember choices saves only on its own button and refreshes without transferring items.");
            Input(true,At(layout.Remember)); Click(layout.Remember);
            Check(GptAssistantService.RememberCalls==1,"Held or repeated Remember clicks cannot save the batch twice.");
            Click(layout.Confirm);
            Check(ChestSortService.Confirms==before+1,"The remembered plan still requires a separate fresh Confirm click to transfer.");
            panel.Draw(batch);
            Check(GptAssistantService.LatestSortSuggestion is null && panel.IsOpen,
                "A completed proposal is consumed while its successful result stays visible.");
            int completedPreviews=ChestSortService.Previews;
            int completedConfirms=ChestSortService.Confirms;
            Check(!GptChatPanel.Buttons.Last(button=>button.Bounds.X==layout.Refresh.X && button.Bounds.Y==layout.Refresh.Y).Enabled,
                "Completed GPT plans visibly disable Refresh instead of offering a generic leftover sort.");
            Click(layout.Refresh); Input(true,At(layout.Refresh)); Click(layout.Confirm); panel.Draw(batch);
            Check(ChestSortService.Previews==completedPreviews && ChestSortService.Confirms==completedConfirms,
                "Clicking or holding completed-plan Refresh cannot rebuild a default plan or move previously unassigned leftovers.");
            Check(string.Join(" ",GptChatPanel.Text).Contains("ask GPT for a new plan"),
                "The completed result explains how to request another explicit GPT plan.");
            GptAssistantService.LatestSortSuggestion = suggestion;
            Physical(false); panel.Close(); panel.Open(suggestion); panel.Draw(batch);
            GptAssistantService.Epoch++;
            before=ChestSortService.Confirms; Input(false,At(layout.Confirm)); Input(true,At(layout.Confirm));
            Check(!panel.IsOpen && ChestSortService.Confirms==before,"Changing conversation epoch rejects a stale plan before any input action.");
            Physical(false); panel.Open(suggestion); panel.Draw(batch);
            Terraria.Main.worldID++; panel.Update(true);
            Check(!panel.IsOpen,"A different world invalidates the open sorting proposal.");
            Physical(false); panel.Open(suggestion); panel.Draw(batch);
            Terraria.Main.LocalPlayer = new(); panel.Update(true);
            Check(!panel.IsOpen,"A different character invalidates the open sorting proposal.");
            GptAssistantService.LatestSortSuggestion = null;
            Physical(false); panel.Open(suggestion);
            Check(!panel.IsOpen,"An expired answer cannot reopen its former sorting plan.");
            GptAssistantService.LatestSortSuggestion = suggestion;
            foreach(var size in new[]{(320,240),(480,270),(640,360)})
            {
                PlayerInput.RealScreenWidth=size.Item1; PlayerInput.RealScreenHeight=size.Item2;
                Physical(false); panel.Open(suggestion); panel.Draw(batch);
                var tight=ChestSortPanelGeometry.Calculate(size.Item1,size.Item2);
                int oldScroll=(int)typeof(ChestSortPanel).GetField("_scroll",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(panel)!;
                PlayerInput.ScrollWheelDeltaForUI=-120; Input(false,new Point(0,0));
                int newScroll=(int)typeof(ChestSortPanel).GetField("_scroll",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(panel)!;
                int visible=Math.Max(1,tight.Body.Height/23);
                Check(newScroll-oldScroll<=visible,$"{size}: one wheel step cannot skip unread rows in a short viewport.");
                panel.Draw(batch);
                int remembersBefore=GptAssistantService.RememberCalls;
                Click(tight.Remember); panel.Draw(batch);
                Check(GptAssistantService.RememberCalls==remembersBefore+1 && ChestSortService.Confirms==before,
                    $"{size}: separate Remember control remains actionable and cannot touch Confirm.");
                Physical(false); panel.Close();
            }
            return passed.ToArray();
        }
    }
}
