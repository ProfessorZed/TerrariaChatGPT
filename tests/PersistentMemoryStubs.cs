#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Terraria
{
    public static class Main { public static bool dedServ, gameMenu; public static int myPlayer; }
    public sealed class Player { public int whoAmI; }
    public sealed class Item
    {
        public int type;
        public string Name = "", Key = "";
        public bool IsAir => type <= 0;
    }
}
namespace Terraria.ID
{
    public static class ContentSamples { public static Dictionary<int, Terraria.Item> ItemsByType = new(); }
}
namespace Terraria.ModLoader
{
    public abstract class ModPlayer
    {
        public Terraria.Player Player = new();
        public virtual void Initialize() { }
        public virtual ModPlayer Clone(Terraria.Player player) { var clone = (ModPlayer)MemberwiseClone(); clone.Player = player; return clone; }
        public virtual void OnEnterWorld() { }
        public virtual void SaveData(IO.TagCompound tag) { }
        public virtual void LoadData(IO.TagCompound tag) { }
    }
}
namespace Terraria.ModLoader.IO
{
    public sealed class TagCompound : Dictionary<string, object>
    {
        public string GetString(string key) => TryGetValue(key, out object? value) ? (string)value : "";
        public int GetInt(string key) => TryGetValue(key, out object? value) ? (int)value : 0;
        public bool GetBool(string key) => TryGetValue(key, out object? value) && (bool)value;
        public IList<T> GetList<T>(string key) => TryGetValue(key, out object? value) ? (IList<T>)value : new List<T>();
    }
}
namespace TerrariaChatGPT.Common.Knowledge
{
    public static class GameReferenceFacts
    {
        public static string Key(Terraria.Item item) => item.Key;
        public static Terraria.Item? ResolveItem(string value) => Terraria.ID.ContentSamples.ItemsByType.Values
            .FirstOrDefault(item => string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Key.Split('/').Last(), value.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
    }
}
