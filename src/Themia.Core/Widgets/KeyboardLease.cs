namespace Themia.Core.Widgets;

/// <summary>
/// What one event did to the keyboard lease: the widget that lost it, the widget that gained it, and
/// whether the keyboard should go back to the window the user was in before the lease began.
/// Both null means nothing changed.
/// </summary>
public readonly record struct LeaseChange<T>(T? Lost, T? Gained, bool GiveBack)
    where T : class
{
    public bool IsNone => Lost is null && Gained is null;
}

/// <summary>
/// [S-800] The keyboard lease: which widget, if any, the keyboard belongs to.
/// <para>
/// The desktop surface is <c>WS_EX_NOACTIVATE</c>, so a click on a widget never pulls focus off the
/// user's work. Two kinds of widget need the keyboard all the same: one that is typed into (a text
/// box got focus) and one whose list is navigated with the arrows (it was clicked). Both are the
/// same thing — the surface takes the keyboard for one widget, for a while — so they are one lease
/// with one holder, and every way it ends (Escape, a click on something else, the surface losing
/// activation, the widget going away, edit mode) goes through the same place.
/// </para>
/// <para>
/// Pure: the shell feeds it events and carries out the <see cref="LeaseChange{T}"/>. The invariant
/// that matters most — the keyboard is never taken without the user asking — is that only
/// <see cref="Press"/> (a click) and <see cref="Type"/> (a text box got focus) can grant it.
/// </para>
/// </summary>
/// <typeparam name="T">The widget, compared by reference.</typeparam>
public sealed class KeyboardLease<T>
    where T : class
{
    /// <summary>The widget holding the keyboard, or null.</summary>
    public T? Holder { get; private set; }

    /// <summary>[S-801] True while the holder's text box has focus (as opposed to its list).</summary>
    public bool Typing { get; private set; }

    /// <summary>
    /// [S-800] A pointer press on <paramref name="widget"/> outside edit mode. A press on the holder
    /// keeps the lease; a press on any other widget ends it, and hands it on when that widget takes
    /// keys. A press on a widget that does not is still a click elsewhere for the holder.
    /// </summary>
    public LeaseChange<T> Press(T widget, bool takesKeys)
    {
        if (ReferenceEquals(Holder, widget))
        {
            return default;
        }

        var lost = Holder;
        Holder = takesKeys ? widget : null;
        Typing = false;
        return new LeaseChange<T>(lost, Holder, GiveBack: false);
    }

    /// <summary>
    /// [S-801] A text box in <paramref name="widget"/> gained (<paramref name="typing"/> true) or lost
    /// focus. Gaining always grants: the box has the caret, so the letters must reach it. Losing gives
    /// the lease up only for a widget that has no keys of its own — a list widget whose editor closed
    /// keeps the arrows, because the user is still in it.
    /// </summary>
    public LeaseChange<T> Type(T widget, bool typing, bool takesKeys)
    {
        if (typing)
        {
            Typing = true;

            if (ReferenceEquals(Holder, widget))
            {
                return default;
            }

            var lost = Holder;
            Holder = widget;
            return new LeaseChange<T>(lost, widget, GiveBack: false);
        }

        if (!ReferenceEquals(Holder, widget) || !Typing)
        {
            return default;
        }

        Typing = false;

        if (takesKeys)
        {
            return default;
        }

        Holder = null;
        return new LeaseChange<T>(widget, null, GiveBack: false);
    }

    /// <summary>
    /// [S-802] Escape reached the shell: the user is done with the widget. The keyboard goes back
    /// where it was before the lease, which a click elsewhere does not need (the click put it there).
    /// </summary>
    public LeaseChange<T> Escape() => End(giveBack: true);

    /// <summary>[S-802] The widget went away (deleted, its view switched off, its display gone).</summary>
    public LeaseChange<T> Remove(T widget) => ReferenceEquals(Holder, widget) ? End(giveBack: false) : default;

    /// <summary>
    /// [S-802] The lease ends without the user handing the keyboard anywhere in particular: the
    /// surface lost activation (a click on the desktop or another app), edit mode began, or the
    /// surface closed.
    /// </summary>
    public LeaseChange<T> Clear() => End(giveBack: false);

    private LeaseChange<T> End(bool giveBack)
    {
        if (Holder is not { } lost)
        {
            return default;
        }

        Holder = null;
        Typing = false;
        return new LeaseChange<T>(lost, null, giveBack);
    }
}
