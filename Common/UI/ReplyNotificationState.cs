#nullable enable
using System;

namespace TerrariaChatGPT.Common.UI;

/// <summary>
/// Read/unread state only. It has no input, game, network or graphics access.
/// Reference identity remains reliable when the bounded transcript removes old rows.
/// </summary>
internal sealed class ReplyNotificationState
{
    public const double PreviewSeconds = 7;
    private bool _initialized;
    private int _epoch;
    private object? _lastReply;
    private double _arrivedAt;
    public bool HasUnread { get; private set; }
    public object? PreviewReply { get; private set; }

    public void Observe(object? latestReply, int epoch, bool gptOpen, double now)
    {
        if (!_initialized || _epoch != epoch)
        {
            Reset();
            _initialized = true;
            _epoch = epoch;
            _lastReply = latestReply;
            return;
        }

        if (!ReferenceEquals(_lastReply, latestReply))
        {
            _lastReply = latestReply;
            if (latestReply is not null && !gptOpen)
            {
                HasUnread = true;
                PreviewReply = latestReply;
                _arrivedAt = double.IsFinite(now) ? now : 0;
            }
        }
        if (gptOpen) MarkRead();
    }

    public float PreviewOpacity(double now)
    {
        if (!HasUnread || PreviewReply is null || !double.IsFinite(now)) return 0;
        double elapsed = Math.Max(0, now - _arrivedAt);
        if (elapsed >= PreviewSeconds) return 0;
        if (elapsed < 0.2) return (float)(elapsed / 0.2);
        return elapsed < 4 ? 1 : (float)((PreviewSeconds - elapsed) / 3);
    }

    public void MarkRead()
    {
        HasUnread = false;
        PreviewReply = null;
        _arrivedAt = 0;
    }

    public void Reset()
    {
        MarkRead();
        _initialized = false;
        _lastReply = null;
    }
}
