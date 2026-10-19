#nullable enable
using System;

namespace TerrariaGPT.Common.Native;

/// <summary>A fixed, display-safe failure. Never attach headers, tokens or upstream bodies.</summary>
public sealed class NativeChatException : Exception
{
    public string Code { get; }
    public NativeChatException(string code, string message) : base(message) { Code = code; }
}
