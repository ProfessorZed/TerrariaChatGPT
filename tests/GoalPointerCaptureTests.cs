#nullable enable
using System;
using System.Collections.Generic;
using TerrariaGPT.Common.UI;

namespace TerrariaGPT.Tests;

public static class GoalPointerCaptureTests
{
    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); }

        var close = new GoalPointerCapture();
        close.BeginFrame(true, false);
        Check(close.LeftPressed, "A native press remains available to the note before masking.");
        close.CaptureHeld();
        Check(close.BlockLeft && !close.BlockRight, "Closing a note owns just the button being held.");
        // The note was removed. Nothing calls CaptureHeld in later frames.
        // Vanilla may set its release flag true after seeing a masked button.
        for (int frame = 0; frame < 5; frame++)
        {
            bool vanillaLeft = true;
            bool vanillaRelease = true;
            close.BeginFrame(true, false);
            if (close.BlockLeft) { vanillaLeft = false; vanillaRelease = false; }
            Check(!vanillaLeft && !vanillaRelease && !close.LeftPressed,
                "A held close gesture cannot synthesize an inventory click after the note disappears.");
        }
        close.BeginFrame(false, false);
        Check(!close.HasCapture && !close.LeftPressed, "Physical release ends capture without synthesizing a new press.");
        close.BeginFrame(true, false);
        Check(close.LeftPressed && !close.HasCapture, "The next genuine click outside notes remains available to the game.");

        var exit = new GoalPointerCapture();
        exit.BeginFrame(false, true);
        exit.CaptureHeld();
        for (int frame = 0; frame < 4; frame++)
        {
            exit.BeginFrame(false, true);
            Check(exit.BlockRight && !exit.BlockLeft && !exit.RightPressed,
                "Moving off a note or hiding its UI cannot leak held stack splitting.");
        }
        exit.BeginFrame(false, false);
        Check(!exit.HasCapture, "A right-button capture clears on physical release.");

        var both = new GoalPointerCapture();
        both.BeginFrame(true, true);
        Check(both.LeftPressed && both.RightPressed, "Simultaneous native button presses are recorded independently.");
        both.CaptureHeld();
        Check(both.BlockLeft && both.BlockRight, "Both held buttons are captured over a note.");
        both.BeginFrame(false, true);
        Check(!both.BlockLeft && both.BlockRight, "Releasing left does not leak a still-held right gesture.");
        both.BeginFrame(true, true);
        Check(both.LeftPressed && !both.BlockLeft && both.BlockRight,
            "An independent new left press outside the note is not stolen by right capture.");
        both.CaptureHeld();
        Check(both.BlockLeft && both.BlockRight, "An active note drag may capture a newly held second button.");
        both.BeginFrame(true, false);
        Check(both.BlockLeft && !both.BlockRight, "Releasing right does not leak a still-held left gesture.");
        both.BeginFrame(false, false);
        Check(!both.HasCapture, "Both released buttons leave no lingering input block.");

        var hover = new GoalPointerCapture();
        hover.BeginFrame(false, false);
        hover.CaptureHeld();
        Check(!hover.HasCapture, "Hovering without a held button cannot capture a later outside click.");
        hover.BeginFrame(true, false);
        Check(hover.LeftPressed && !hover.BlockLeft, "A new outside press after hover reaches the game.");
        hover.BeginFrame(true, false);
        hover.CaptureHeld();
        Check(!hover.LeftPressed && hover.BlockLeft, "Entering a note while already held blocks underlying actions without clicking the note.");
        return passed;
    }
}
