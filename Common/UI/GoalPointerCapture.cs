#nullable enable
namespace TerrariaGPT.Common.UI;

/// <summary>
/// A note owns a mouse gesture until its physical release. Vanilla may rearm
/// release flags while held buttons are masked, so those flags are not used to
/// detect note clicks or decide when ownership ends.
/// </summary>
internal sealed class GoalPointerCapture
{
    private bool _leftDown;
    private bool _rightDown;
    public bool LeftPressed { get; private set; }
    public bool RightPressed { get; private set; }
    public bool BlockLeft { get; private set; }
    public bool BlockRight { get; private set; }
    public bool HasCapture => BlockLeft || BlockRight;

    public void BeginFrame(bool nativeLeftDown, bool nativeRightDown)
    {
        LeftPressed = nativeLeftDown && !_leftDown;
        RightPressed = nativeRightDown && !_rightDown;
        _leftDown = nativeLeftDown;
        _rightDown = nativeRightDown;
        if (!nativeLeftDown) BlockLeft = false;
        if (!nativeRightDown) BlockRight = false;
    }

    public void CaptureHeld()
    {
        BlockLeft |= _leftDown;
        BlockRight |= _rightDown;
    }
}
