using Terraria.ModLoader;
using TerrariaGPT.Common.Players;
using TerrariaGPT.Common.Services;
using TerrariaGPT.Common.Systems;

namespace TerrariaGPT.Common.Commands;

public sealed class GptCommand : ModCommand
{
    public override string Command => "gpt";
    public override CommandType Type => CommandType.Chat;
    public override string Usage => "/gpt [question]";
    public override string Description => "Open the private GPT chat tab.";
    public override void Action(CommandCaller caller, string input, string[] args) => GptChatSystem.OpenGpt(string.Join(" ", args));
}

public sealed class GptClaimCommand : ModCommand
{
    public override string Command => "gptclaim";
    public override CommandType Type => CommandType.Chat;
    public override string Usage => "/gptclaim";
    public override string Description => "Resume remembering a forgotten open chest (single-player; other opened chests are remembered automatically).";
    public override void Action(CommandCaller caller, string input, string[] args)
    {
        GptAssistantService.AddNotice(caller.Player.GetModPlayer<GptKnowledgePlayer>().ClaimOpenChest());
        GptChatSystem.OpenGpt();
    }
}

public sealed class GptForgetCommand : ModCommand
{
    public override string Command => "gptforget";
    public override CommandType Type => CommandType.Chat;
    public override string Usage => "/gptforget";
    public override string Description => "Forget the open chest and skip it until /gptclaim is used.";
    public override void Action(CommandCaller caller, string input, string[] args)
    {
        GptAssistantService.AddNotice(caller.Player.GetModPlayer<GptKnowledgePlayer>().ForgetOpenChest());
        GptChatSystem.OpenGpt();
    }
}
