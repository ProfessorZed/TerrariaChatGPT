#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria.GameInput;
using TerrariaChatGPT.Common.Memory;
using TerrariaChatGPT.Common.Players;
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
    public sealed class Player { public bool mouseInterface, controlUseItem = true, controlUseTile = true; public GptMemoryPlayer Memory = new(); public T GetModPlayer<T>() where T : class => (Memory as T)!; }
    public static class Main
    {
        public static bool dedServ, gameMenu, mouseLeft, mouseLeftRelease = true, mouseRight, mouseRightRelease = true;
        public static float UIScale = 1;
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

namespace TerrariaChatGPT.Common.Players
{
    public sealed class GptMemoryPlayer
    {
        public bool Enabled = true;
        public long Revision;
        public readonly List<MemoryEntry> Entries = new();
        public int ClearCalls, RemoveCalls, ToggleCalls;
        public void SetEnabled(bool enabled) { Enabled=enabled; Revision++; ToggleCalls++; }
        public void Clear() { Entries.Clear(); Revision++; ClearCalls++; }
        public bool Forget(string id) { int removed=Entries.RemoveAll(entry=>entry.Id==id); if(removed>0)Revision++; RemoveCalls++; return removed>0; }
    }
}
namespace TerrariaChatGPT.Tests
{
    public static class MemoryPanelInputTests
    {
        public static string[] Run()
        {
            var passed=new List<string>();
            void Check(bool ok,string message) { if(!ok)throw new Exception(message); passed.Add(message); }
            static Point At(ChatRect r)=>new(r.X+2,r.Y+2);
            static void Physical(bool left,bool right=false)
            {
                Mouse.State=new(left?ButtonState.Pressed:ButtonState.Released,right?ButtonState.Pressed:ButtonState.Released);
                Terraria.Main.mouseLeft=left; Terraria.Main.mouseRight=right;
                Terraria.Main.mouseLeftRelease=true; Terraria.Main.mouseRightRelease=true;
            }
            var memory=Terraria.Main.LocalPlayer.Memory;
            memory.Entries.Add(new("1","preference","playstyle","I prefer ranged weapons.","","","",DateTimeOffset.UtcNow));
            memory.Entries.Add(new("2","sorting","","Glass belongs in Building supplies.","","Terraria/Glass","chest:Building supplies",DateTimeOffset.UtcNow));
            memory.Entries.Add(new("hooks","sorting","","Grappling hooks belong with travel supplies.","","group:grappling_hooks","travel",DateTimeOffset.UtcNow));
            memory.Entries.Add(new("goodies","sorting","","Goodie Bags belong with vanity.","","Terraria/GoodieBag","vanity",DateTimeOffset.UtcNow));
            var panel=new MemoryPanel(); var batch=new SpriteBatch();
            var layout=MemoryPanelGeometry.Calculate(1280,720);
            void Input(bool left,Point point,bool escape=false,int page=0,bool right=false)
            { Physical(left,right); panel.HandleInput(point,escape,page); }
            void Click(ChatRect rect) { Input(false,At(rect)); Input(true,At(rect)); }
            Physical(true); panel.Open(); panel.Draw(batch);
            Input(true,At(layout.Clear)); Input(true,At(layout.Toggle));
            Check(memory.ClearCalls==0&&memory.ToggleCalls==0,"Opening or holding the opening click cannot mutate memory.");
            Check(string.Join(" ",GptChatPanel.Text).Contains("Building supplies"),"Sorting rules show the saved item destination literally.");
            Check(string.Join(" ",GptChatPanel.Text).Contains("Chest: Building supplies"),"Exact chest preferences are labeled as Chest rather than category.");
            Check(string.Join(" ",GptChatPanel.Text).Contains("All Grappling Hooks -> Category: Travel"),"Item-family memories display a readable family and category destination.");
            Check(string.Join(" ",GptChatPanel.Text).Contains("GoodieBag -> Category: Vanity"),"Vanity sorting memory is explicitly shown as a category rather than an exact chest name.");
            Input(false,At(layout.Clear),right:true);
            Check(memory.ClearCalls==0&&!Terraria.Main.mouseRight&&!Terraria.Main.mouseRightRelease,"Right click cannot clear or leak into inventory actions.");
            int locks=PlayerInput.ScrollLocks; PlayerInput.ScrollWheelDeltaForUI=-120; Input(false,new(0,0));
            Check(PlayerInput.ScrollWheelDeltaForUI==0&&PlayerInput.ScrollLocks>locks,"Memory modal consumes the wheel and reserves hotbar scrolling outside its bounds.");
            panel.Draw(batch); Click(layout.Clear); panel.Draw(batch);
            Check(memory.ClearCalls==0&&GptChatPanel.Buttons.Last(b=>b.Bounds.X==layout.Clear.X).Text=="Confirm clear","Clear all first shows an explicit second confirmation.");
            Input(true,At(layout.Clear));
            Check(memory.ClearCalls==0,"Holding the first Clear click cannot confirm deletion.");
            memory.Revision++; Input(false,At(layout.Clear)); Input(true,At(layout.Clear));
            Check(memory.ClearCalls==0,"A changed memory revision invalidates a drawn clear confirmation.");
            panel.Draw(batch); Click(layout.Clear); panel.Draw(batch); Click(layout.Close);
            Check(!panel.IsOpen&&memory.ClearCalls==0,"Closing cancels a pending clear-all confirmation.");
            Physical(false); panel.Update(false); panel.Open(); panel.Draw(batch); Click(layout.Clear); panel.Draw(batch); Click(layout.Clear); panel.Draw(batch);
            Check(memory.ClearCalls==1&&memory.Entries.Count==0,"Two distinct fresh reviewed Clear clicks clear this character's memories once.");
            Click(layout.Clear);
            Check(memory.ClearCalls==1&&!GptChatPanel.Buttons.Last(b=>b.Text=="Clear all").Enabled,"Empty memory cannot be cleared repeatedly.");
            memory.Entries.Add(new("3","preference","style","Brief answers please.","","","",DateTimeOffset.UtcNow)); memory.Revision++;
            panel.Draw(batch); Click(layout.Toggle); panel.Draw(batch);
            Check(!memory.Enabled&&memory.Entries.Count==1,"Pausing memory preserves the saved list.");
            Click(layout.Toggle); panel.Draw(batch);
            Check(memory.Enabled&&memory.ToggleCalls==2,"Memory can be re-enabled without recreating preferences.");
            var remove=GptChatPanel.Buttons.Last(b=>b.Text=="Remove").Bounds;
            Input(false,new(remove.X+2,remove.Y+2));
            PlayerInput.RealScreenWidth=640; PlayerInput.RealScreenHeight=360;
            Input(true,new(remove.X+2,remove.Y+2));
            Check(memory.RemoveCalls==0,"Resizing invalidates stale Remove hitboxes.");
            panel.Draw(batch); remove=GptChatPanel.Buttons.Last(b=>b.Text=="Remove").Bounds;
            Input(false,new(remove.X+2,remove.Y+2)); PlayerInput.ScrollWheelDeltaForUI=-120; Input(true,new(remove.X+2,remove.Y+2));
            Check(memory.RemoveCalls==0,"Scrolling and clicking in one update cannot remove a different row.");
            panel.Draw(batch); remove=GptChatPanel.Buttons.Last(b=>b.Text=="Remove").Bounds;
            Click(new(remove.X,remove.Y,remove.Width,remove.Height)); panel.Draw(batch);
            Check(memory.RemoveCalls==1&&memory.Entries.Count==0,"A fresh Remove click deletes only the displayed entry.");
            Input(true,new(0,0),escape:true);
            Check(!panel.IsOpen&&panel.OwnsPointer&&!Terraria.Main.mouseLeft,"Escape closes but retains ownership of a held gesture.");
            Physical(true); panel.Update(false);
            Check(panel.OwnsPointer&&!Terraria.Main.mouseLeft,"Closing cannot leak a held click to the game on a later frame.");
            Physical(false); panel.Update(false);
            Check(!panel.OwnsPointer,"Physical release restores normal input.");
            panel.Open(); Terraria.Main.LocalPlayer=new Terraria.Player(); panel.Update(true);
            Check(!panel.IsOpen,"Changing character invalidates the memory manager immediately.");
            panel.Open(); panel.Update(false);
            Check(!panel.IsOpen,"Closing GPT discards manager controls and pending confirmations.");
            PlayerInput.RealScreenWidth=1280; PlayerInput.RealScreenHeight=720;
            var otherMemory=Terraria.Main.LocalPlayer.Memory;
            otherMemory.Entries.Add(new("weapons","sorting","","Weapons belong in Travel.","","group:weapon","travel",DateTimeOffset.UtcNow));
            otherMemory.Entries.Add(new("potions","sorting","","Potions belong in Potions & Food.","","group:potion","chest:Potions & Food",DateTimeOffset.UtcNow));
            otherMemory.Revision++; GptChatPanel.Text.Clear(); panel.Open(); panel.Draw(batch);
            Check(string.Join(" ",GptChatPanel.Text).Contains("All Weapons -> Category: Travel"),"Every supported item family uses its public display name.");
            Check(string.Join(" ",GptChatPanel.Text).Contains("All Potions -> Chest: Potions & Food"),"Generic family display preserves exact combined chest labels.");
            foreach(var size in new[]{(1280,720),(640,360),(480,240),(200,100),(1,1)})
            {
                var geometry=MemoryPanelGeometry.Calculate(size.Item1,size.Item2);
                Check(geometry.Panel.X>=0&&geometry.Panel.Y>=0&&geometry.Panel.Right<=size.Item1&&geometry.Panel.Bottom<=size.Item2,
                    $"Memory modal stays within {size.Item1}x{size.Item2} viewport.");
            }
            return passed.ToArray();
        }
    }
}

