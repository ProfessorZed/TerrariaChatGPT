#nullable enable
namespace TerrariaChatGPT.Common.Services;
public enum ChatGptConnectionAction { Status, SignIn, SignOut, Welcome, CopyUsageLink, CopySignInLink, CancelSignIn }
public sealed record ChatGptConnectionStatus(bool Connected, bool SigningIn, string Account,
    string Message, bool UsingPlan, bool WelcomePending, bool IsError = false,
    bool CanCopySignInLink = false, string ClipboardLink = "");
