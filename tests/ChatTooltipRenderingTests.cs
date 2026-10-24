#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ModLoader;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Knowledge;
using TerrariaChatGPT.Common.Services;
using TerrariaChatGPT.Common.Sorting;
using TerrariaChatGPT.Common.UI;

namespace Microsoft.Xna.Framework
{
    public readonly record struct Point(int X,int Y);
    public readonly record struct Vector3(float X,float Y,float Z);
    public readonly record struct Vector2(float X,float Y)
    { public static Vector2 Zero=>new(0,0); public static Vector2 operator+(Vector2 a,Vector2 b)=>new(a.X+b.X,a.Y+b.Y); }
    public readonly record struct Color(int R,int G,int B,int A=255)
    { public static Color operator*(Color c,float f)=>new((int)(c.R*f),(int)(c.G*f),(int)(c.B*f),(int)(c.A*f)); }
    public readonly record struct Rectangle(int X,int Y,int Width,int Height)
    {
        public int Left=>X; public int Top=>Y; public int Right=>X+Width; public int Bottom=>Y+Height;
        public Point Center=>new(X+Width/2,Y+Height/2);
        public bool Contains(Point p)=>p.X>=X&&p.Y>=Y&&p.X<Right&&p.Y<Bottom;
        public static Rectangle Intersect(Rectangle a,Rectangle b)=>new(Math.Max(a.X,b.X),Math.Max(a.Y,b.Y),
            Math.Max(0,Math.Min(a.Right,b.Right)-Math.Max(a.X,b.X)),Math.Max(0,Math.Min(a.Bottom,b.Bottom)-Math.Max(a.Y,b.Y)));
    }
}
namespace Microsoft.Xna.Framework.Graphics
{
    public enum SpriteEffects { None }
    public sealed class Texture2D { public int Page=-1; }
    public sealed record DrawOperation(string Kind,Rectangle Bounds,Color Color,Vector2 Position=default,int Page=-1,Rectangle Source=default,float Scale=1);
    public sealed class SpriteBatch
    {
        public readonly List<DrawOperation> Operations=new();
        public void Draw(Texture2D texture,Rectangle bounds,Color color)=>Operations.Add(new("box",bounds,color));
        public void Draw(Texture2D texture,Vector2 position,Rectangle source,Color color,float rotation,Vector2 origin,float scale,SpriteEffects effects,float depth)
            =>Operations.Add(new("glyph",default,color,position,texture.Page,source,scale));
    }
}
namespace Microsoft.Xna.Framework.Input
{
    public readonly record struct MouseState(int X,int Y);
    public static class Mouse { public static MouseState State; public static MouseState GetState()=>State; }
}
namespace ReLogic.Graphics
{
    public sealed class DynamicSpriteFont
    {
        public sealed class SpriteCharacterData
        { public Vector3 Kerning=new(0,8,0); public Rectangle Padding; public Rectangle Glyph=new(0,0,8,16); public Texture2D Texture=new(); }
        public readonly Dictionary<char,SpriteCharacterData> SpriteCharacters=new();
        public SpriteCharacterData DefaultCharacterData=new(); public float CharacterSpacing; public int LineSpacing=24;
        public Vector2 MeasureString(string text)
        {
            float pen=0; bool first=true;
            foreach(char c in text) { var g=SpriteCharacters.TryGetValue(c,out var v)?v:DefaultCharacterData;
                pen+=first?Math.Max(g.Kerning.X,0):CharacterSpacing+g.Kerning.X; pen+=g.Kerning.Y+g.Kerning.Z; first=false; }
            return new(pen,LineSpacing);
        }
    }
}
namespace ReLogic.OS
{
    public interface IClipboard { string Value {get;set;} }
    public static class Platform { public static T Get<T>()=>throw new NotSupportedException(); }
}
namespace Terraria
{
    public sealed class Player { public T GetModPlayer<T>() where T:new()=>new T(); }
    public static class Main { public static float UIScale=1; public static ulong GameUpdateCount; public static Player LocalPlayer=new(); }
}
namespace Terraria.GameInput
{ public static class PlayerInput { public static int RealScreenWidth=1280,RealScreenHeight=720; } }
namespace Terraria.GameContent
{
    public sealed class Asset<T> where T:new() { public T Value=new(); }
    public static class FontAssets { public static Asset<ReLogic.Graphics.DynamicSpriteFont> MouseText=new(); }
    public static class TextureAssets { public static Asset<Texture2D> MagicPixel=new(); }
}
namespace Terraria.ModLoader
{ public static class ModContent { public static T GetInstance<T>() where T:new()=>Holder<T>.Value; private static class Holder<T> where T:new() { internal static T Value=new(); } } }
namespace TerrariaChatGPT { public sealed class TerrariaChatGPTMod { public Version Version=new(0,9,9); } }
namespace TerrariaChatGPT.Common.Config
{
    public enum GptProvider { ChatGPT,WikiOnly,OpenAiApi }
    public sealed class GptClientConfig { public float ChatTextScale=.9f; public int ChatPanelWidth=820,ChatPanelHeight=390; }
}
namespace TerrariaChatGPT.Common.Players
{
    public sealed class GptMemoryPlayer { public bool Undo(string id,out string message) { message="Undone"; return true; } }
    public sealed class GptProgressPlayer { public bool TrySetGoal(object goal,out object? value,out string message) { value=null; message="Saved"; return true; } }
}
namespace TerrariaChatGPT.Common.Services
{
    public sealed record ChatGptConnectionStatus(bool Connected=false,bool SigningIn=false,bool UsingPlan=false,bool IsError=false);
    public sealed record ChatEntry(string Role,string Text)
    {
        public DateTimeOffset Timestamp=DateTimeOffset.UtcNow;
        public IReadOnlyList<AnswerHighlight> Highlights=Array.Empty<AnswerHighlight>();
        public IReadOnlyList<WebCitation> Citations=Array.Empty<WebCitation>();
        public object? GoalSuggestion; public bool WebSearched; public string? MemoryChangeId,MemorySummary;
        public SortSuggestion? SortSuggestion;
    }
    public static class GptAssistantService
    {
        public static IReadOnlyList<ChatEntry> Messages=Array.Empty<ChatEntry>();
        public static SortSuggestion? LatestSortSuggestion;
        public static bool IsCurrentSortSuggestion(SortSuggestion suggestion)=>ReferenceEquals(suggestion,LatestSortSuggestion);
    }
}
namespace TerrariaChatGPT.Tests
{
    public static class ChatTooltipRenderingTests
    {
        private static readonly Color TooltipBorder=new(115,148,176);
        public static IReadOnlyList<string> Run()
        {
            var passed=new List<string>();
            void Check(bool valid,string message) { if(!valid)throw new InvalidOperationException(message); passed.Add(message); }
            foreach(int width in new[]{480,820})
            {
                ModContent.GetInstance<GptClientConfig>().ChatPanelWidth=width;
                var panel=new GptChatPanel(); var layout=GptChatPanel.GetLayout(); var batch=new SpriteBatch();
                var badge=ModReleaseNotes.Badge(new(layout.Panel.X,layout.Panel.Y,layout.Panel.Width,layout.Panel.Height),
                    new(layout.Memory.X,layout.Memory.Y,layout.Memory.Width,layout.Memory.Height),default);
                Microsoft.Xna.Framework.Input.Mouse.State=new(badge.X+5,badge.Y+5);
                panel.Draw(batch,"",Array.Empty<ChatEntry>(),false,GptProvider.ChatGPT,new(),false);
                Check(!batch.Operations.Any(IsTooltip),$"{width}: panel content defers its tooltip until the overlay pass.");
                // Simulate the compositor's later toolbar pass using the actual shared button renderer.
                GptChatPanel.DrawButton(batch,layout.ModelSelector,"Model: Auto",true,.64f);
                GptChatPanel.DrawButton(batch,layout.EffortSelector,"Effort: Fast",true,.64f);
                int toolbarEnd=batch.Operations.Count;
                panel.DrawTooltips(batch);
                int tipIndex=batch.Operations.FindIndex(IsTooltip);
                Check(tipIndex>=toolbarEnd,$"{width}: patch notes draw after both model controls.");
                Rectangle tip=batch.Operations[tipIndex].Bounds;
                Check(tip.X>=0&&tip.Y>=0&&tip.Right<=1280&&tip.Bottom<=720,$"{width}: patch notes stay within screen bounds.");
                Check(Rectangle.Intersect(tip,layout.ModelSelector).Height>0,$"{width}: fixture covers the reported overlapping model-control area.");
                Microsoft.Xna.Framework.Input.Mouse.State=new(0,0);
                batch.Operations.Clear(); panel.Draw(batch,"",Array.Empty<ChatEntry>(),false,GptProvider.ChatGPT,new(),false); panel.DrawTooltips(batch);
                Check(!batch.Operations.Any(IsTooltip),$"{width}: leaving the version badge clears the previous tooltip.");
                Microsoft.Xna.Framework.Input.Mouse.State=new(badge.X+5,badge.Y+5);
                panel.Draw(batch,"",Array.Empty<ChatEntry>(),false,GptProvider.ChatGPT,new(),false);
                ModContent.GetInstance<GptClientConfig>().ChatPanelWidth=width+10;
                batch.Operations.Clear(); panel.DrawTooltips(batch);
                Check(batch.Operations.Count==0,$"{width}: resized stale hover geometry cannot render a tooltip.");
            }
            foreach(var size in new[]{(1280,720),(640,360),(240,120)})
            {
                PlayerInput.RealScreenWidth=size.Item1; PlayerInput.RealScreenHeight=size.Item2;
                var batch=new SpriteBatch(); GptChatPanel.DrawLiteralTooltip(batch,ModReleaseNotes.ForVersion(new(0,9,8)),new(size.Item1-1,size.Item2-1));
                var bounds=batch.Operations.First(IsTooltip).Bounds;
                Check(bounds.X>=0&&bounds.Y>=0&&bounds.Right<=size.Item1&&bounds.Bottom<=size.Item2,$"{size}: edge tooltip is clamped inside the viewport.");
            }
            PlayerInput.RealScreenWidth=1280; PlayerInput.RealScreenHeight=720;
            Check(ModReleaseNotes.ForVersion(new(0,9,9)).Split('\n').Length==4,"Current release has exactly three concise notes.");
            Check(ModReleaseNotes.ForVersion(new(0,9,10)).Contains("No embedded notes"),"Unknown builds cannot display stale release notes.");
            foreach(var size in new[]{(320,240),(480,270),(640,360),(1280,720)})
            {
                PlayerInput.RealScreenWidth=size.Item1; PlayerInput.RealScreenHeight=size.Item2;
                ModContent.GetInstance<GptClientConfig>().ChatPanelWidth=480;
                var suggestion=new SortSuggestion(Array.AsReadOnly(new[]{new SortAssignment("Terraria/AmethystHook","Tools & Travel")}));
                var entry=new ChatEntry("assistant","Amethyst Hook goes to Tools & Travel.") { SortSuggestion=suggestion, GoalSuggestion=new(), WebSearched=true };
                GptAssistantService.Messages=new[]{entry}; GptAssistantService.LatestSortSuggestion=suggestion;
                var panel=new GptChatPanel(); var batch=new SpriteBatch();
                panel.Draw(batch,"",GptAssistantService.Messages,false,GptProvider.ChatGPT,new(),false);
                var actions=(System.Collections.IEnumerable)typeof(GptChatPanel).GetField("_actions",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(panel)!;
                object sortHit=actions.Cast<object>().Single(action=>(bool)action.GetType().GetProperty("Sort")!.GetValue(action)!);
                var bounds=(Rectangle)sortHit.GetType().GetProperty("Bounds")!.GetValue(sortHit)!;
                var clip=GptChatPanel.GetLayout().Transcript;
                Check(bounds.Width>0&&bounds.Height>0&&Rectangle.Intersect(clip,bounds)==bounds,$"{size}: Review sort has a visible, clipped, independent hit target.");
                int reviews=0;
                panel.HandleClick(bounds.Center,proposal=>{ if(ReferenceEquals(suggestion,proposal)) reviews++; });
                Check(reviews==1,$"{size}: the answer button opens exactly its accepted mapping.");
                GptAssistantService.LatestSortSuggestion=null;
                panel.HandleClick(bounds.Center,_=>reviews++);
                Check(reviews==1,$"{size}: superseded plan cannot reopen through a previously drawn hit target.");
                GptAssistantService.LatestSortSuggestion=suggestion;
                PlayerInput.RealScreenWidth=size.Item1+80;
                PlayerInput.RealScreenHeight=size.Item2+80;
                panel.HandleClick(bounds.Center,_=>reviews++);
                Check(reviews==1,$"{size}: resizing invalidates the old Review sort hit target.");
            }
            GptAssistantService.Messages=Array.Empty<ChatEntry>(); GptAssistantService.LatestSortSuggestion=null;
            PlayerInput.RealScreenWidth=1280; PlayerInput.RealScreenHeight=720;
            return passed;
        }
        private static bool IsTooltip(DrawOperation op)=>op.Kind=="box"&&op.Color==TooltipBorder;
        public static string Export(string fontJson,int width,int height,int preferred)
        {
            using var document=JsonDocument.Parse(fontJson); var source=document.RootElement; var font=FontAssets.MouseText.Value;
            font.LineSpacing=source.GetProperty("lineSpacing").GetInt32(); font.CharacterSpacing=source.GetProperty("spacing").GetSingle();
            foreach(var row in source.GetProperty("glyphs").EnumerateArray())
            {
                var m=row.GetProperty("metrics"); var p=row.GetProperty("padding"); var g=row.GetProperty("glyph");
                font.SpriteCharacters[(char)row.GetProperty("character").GetInt32()]=new()
                { Kerning=new(m[0].GetSingle(),m[1].GetSingle(),m[2].GetSingle()), Padding=new(p[0].GetInt32(),p[1].GetInt32(),p[2].GetInt32(),p[3].GetInt32()),
                    Glyph=new(g[0].GetInt32(),g[1].GetInt32(),g[2].GetInt32(),g[3].GetInt32()), Texture=new(){Page=row.GetProperty("page").GetInt32()} };
            }
            font.DefaultCharacterData=font.SpriteCharacters[(char)source.GetProperty("defaultCharacter").GetInt32()];
            PlayerInput.RealScreenWidth=width; PlayerInput.RealScreenHeight=height; ModContent.GetInstance<GptClientConfig>().ChatPanelWidth=preferred;
            var panel=new GptChatPanel(); var layout=GptChatPanel.GetLayout(); var batch=new SpriteBatch();
            Microsoft.Xna.Framework.Input.Mouse.State=new(layout.Panel.X+70,layout.Panel.Y+14);
            panel.Draw(batch,"",Array.Empty<ChatEntry>(),false,GptProvider.ChatGPT,new(Connected:true,UsingPlan:true),false);
            GptChatPanel.DrawButton(batch,layout.ModelSelector,"Model: Auto",true,.64f);
            GptChatPanel.DrawButton(batch,layout.EffortSelector,"Effort: Fast",true,.64f);
            panel.DrawTooltips(batch);
            return JsonSerializer.Serialize(new{width,height,operations=batch.Operations});
        }
    }
}
