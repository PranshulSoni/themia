namespace Themia.Core.Widgets;

/// <summary>Which pass of a routed key event a decision is for.</summary>
public enum KeyPhase
{
    /// <summary>The tunnelling pass (<c>PreviewKeyDown</c>), root to focused element: before any control.</summary>
    Preview,

    /// <summary>The bubbling pass (<c>KeyDown</c>), focused element to root: only what no control handled.</summary>
    Bubble,
}

/// <summary>What the surface does with one key press while a widget holds the keyboard lease.</summary>
public enum LeaseKeyRoute
{
    /// <summary>Not the lease's: the focused control (or nothing) has it.</summary>
    Pass,

    /// <summary>Offer it to the holder's view (<c>IWidgetKeys.HandleKey</c>); it says whether it used it.</summary>
    ToHolder,

    /// <summary>End the lease (and hand the keyboard back).</summary>
    EndLease,
}

/// <summary>
/// [S-860] Where a key goes while a widget holds the lease. The holder gets its keys in the
/// <b>tunnelling</b> pass, before any control inside the card: a <c>ScrollViewer</c> handles the
/// arrows, Page Up/Down and Home/End itself in the bubbling pass and marks them handled, so a lease
/// that only listened to the bubble never saw them and list navigation never worked in any widget
/// whose rows scroll. Two things still go to the control first: everything while a text editor has
/// focus (the box's typing, caret and selection keys are the box's, [S-804]), and Escape, which ends
/// the lease only after the bubble, so a box that clears on Escape keeps it.
/// </summary>
public static class LeaseKeyRouting
{
    /// <param name="phase">The pass the key is in.</param>
    /// <param name="key">The key as <see cref="WidgetKeys.FromVirtualKey"/> maps it; null = no widget key.</param>
    /// <param name="textFocused">Whether a text editor on the surface has focus.</param>
    public static LeaseKeyRoute Decide(KeyPhase phase, WidgetKey? key, bool textFocused)
    {
        if (key is not { } widgetKey)
        {
            return LeaseKeyRoute.Pass;
        }

        if (widgetKey == WidgetKey.Escape)
        {
            return phase == KeyPhase.Bubble ? LeaseKeyRoute.EndLease : LeaseKeyRoute.Pass;
        }

        return phase == KeyPhase.Preview && !textFocused ? LeaseKeyRoute.ToHolder : LeaseKeyRoute.Pass;
    }
}
