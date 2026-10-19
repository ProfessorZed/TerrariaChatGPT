#nullable enable
using System;
using TerrariaGPT.Common.Goals;

namespace TerrariaGPT.Common.Players;

public sealed class GptProgressPlayer
{
    public static ProgressSnapshot Snapshot = new(DateTimeOffset.UnixEpoch,
        Array.Empty<ProgressItem>(), Array.Empty<RememberedProgressItem>(), Array.Empty<ProgressFlag>(),
        Array.Empty<ProgressFlag>(), Array.Empty<ProgressFlag>(), Array.Empty<ProgressSubject>(),
        Array.Empty<NpcProgress>(), Array.Empty<string>());
    public ProgressSnapshot CreateSnapshot() => Snapshot;
}
