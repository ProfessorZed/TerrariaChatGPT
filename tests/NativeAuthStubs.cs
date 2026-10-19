#nullable enable
namespace TerrariaChatGPT.Common.Services;
public enum ChatGptConnectionAction { Status, SignIn, SignOut, Welcome, ManageUsage }
public sealed record ChatGptConnectionStatus(bool Connected, bool SigningIn, string Account,
    string Message, bool UsingPlan, bool WelcomePending, bool IsError = false);
