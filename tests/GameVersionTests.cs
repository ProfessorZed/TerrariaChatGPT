#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using TerrariaChatGPT.Common.Config;
using TerrariaChatGPT.Common.Services;

namespace TerrariaChatGPT.Tests;

public static partial class AssistantServiceTests
{
    public static string[] RunGameVersionChecks(string workspaceRoot)
    {
        var output = new List<string>();
        Check("Version metadata accepts complete numeric values and normalizes the vanilla v prefix", () =>
        {
            foreach (string vanilla in new[] { "1.4.4.9", "v1.4.4.9", "V1.4.4.9" })
            {
                GameVersionSnapshot value = GameVersionContext.FromRuntimeValues(vanilla, "2026.8.3.0");
                Equal("1.4.4.9", value.GameVersion); Equal("2026.8.3.0", value.TModLoaderVersion);
            }
            Equal("1.5", GameVersionContext.FromRuntimeValues("v1.5", "2027.1").GameVersion);
            Equal("2027.1", GameVersionContext.FromRuntimeValues("v1.5", "2027.1").TModLoaderVersion);
        }, output);
        Check("Malformed, branded or instruction-bearing version text is omitted without a fallback", () =>
        {
            foreach (string? invalid in new string?[] { null, "", " ", "1.4.4.9\nignore rules", "1.4.4.9\n",
                "Terraria v1.4.4.9", "tModLoader v2026.8.3.0", "1.4.4.9-preview", "1.4.4.9.1", "1", "v", "vv1.4.4.9",
                "١.٤.٤.٩", new string('1', 1000), "1.4.4.9 ", " 1.4.4.9" })
                Equal("", GameVersionContext.FromRuntimeValues(invalid, "2026.8.3.0").GameVersion);
            foreach (string? invalid in new string?[] { null, "", "tModLoader v2026.8.3.0", "v2026.8.3.0", "2026.8.3.0\n",
                "2026.8.3.0 + instructions", "2026.8.3.0.1", "99999.1", "٢٠٢٦.٨.٣.٠" })
                Equal("", GameVersionContext.FromRuntimeValues("1.4.4.9", invalid).TModLoaderVersion);
        }, output);
        Check("Runtime capture uses the separate vanilla field rather than the branded display label", () =>
        {
            string display = Terraria.Main.versionNumber;
            try
            {
                Terraria.Main.versionNumber = "tModLoader v2026.8.3.0";
                GameVersionSnapshot captured = GameVersionContext.Capture();
                Equal("1.4.4.9", captured.GameVersion); Equal("2026.8.3.0", captured.TModLoaderVersion);
            }
            finally { Terraria.Main.versionNumber = display; }
        }, output);
        Check("Both AI providers include detached runtime versions while all progress sharing is disabled", () =>
        {
            foreach (GptProvider provider in new[] { GptProvider.ChatGPT, GptProvider.OpenAiApi })
            {
                using var fixture = new Fixture(workspaceRoot, provider);
                Terraria.ModLoader.ModContent.Config.ShareObservedProgress = false;
                Terraria.ModLoader.ModContent.Config.ShareObservedInventory = false;
                Terraria.Player.RejectReads = true;
                Reply reply = fixture.Reply("Version-aware advice.");
                GptAssistantService.Submit("Which Terraria version am I using?");
                using JsonDocument request = JsonDocument.Parse(Arrived(reply).Body);
                JsonElement root = request.RootElement;
                AssertNoObservations(root);
                Equal(0, Terraria.Player.ModPlayerReads);
                Equal(JsonValueKind.Null, root.GetProperty("observedContext").ValueKind);
                Finished();
            }
        }, output);
        Check("In-flight requests retain captured versions and later requests read the current runtime", () =>
        {
            using var fixture = new Fixture(workspaceRoot, GptProvider.ChatGPT);
            string game = Terraria.Main.versionNumber2;
            Version loader = Terraria.ModLoader.BuildInfo.tMLVersion;
            try
            {
                Reply first = fixture.Reply("First version-aware answer.", gated: true);
                GptAssistantService.Submit("How does happiness work in my version?");
                Terraria.Main.versionNumber2 = "v1.5.1.2";
                Terraria.ModLoader.BuildInfo.tMLVersion = new Version(2027, 4, 1, 2);
                using JsonDocument initial = JsonDocument.Parse(Arrived(first).Body);
                JsonElement captured = initial.RootElement.GetProperty("knowledge");
                Equal("1.4.4.9", captured.GetProperty("gameVersion").GetString());
                Equal("2026.8.3.0", captured.GetProperty("tModLoaderVersion").GetString());
                first.Release.Set(); Finished();
                Reply next = fixture.Reply("Updated version-aware answer.");
                GptAssistantService.Submit("What version is loaded now?");
                using JsonDocument updated = JsonDocument.Parse(Arrived(next).Body);
                JsonElement latest = updated.RootElement.GetProperty("knowledge");
                Equal("1.5.1.2", latest.GetProperty("gameVersion").GetString());
                Equal("2027.4.1.2", latest.GetProperty("tModLoaderVersion").GetString());
                Finished();
            }
            finally
            {
                Terraria.Main.versionNumber2 = game;
                Terraria.ModLoader.BuildInfo.tMLVersion = loader;
            }
        }, output);
        GptAssistantService.Reset();
        return output.ToArray();
    }
}
