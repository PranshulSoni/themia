using Microsoft.UI.Xaml;
using Themia.App.Services;
using Themia.App.Shell;
using Themia.Core.Model;

namespace Themia.App.Widgets;

/// <summary>
/// What a widget view is allowed to ask of the surface it is drawn on.
/// <para>
/// Almost no widget needs this: the settings window is the writer, and the card owns input. The two
/// that do — a note, whose text <i>is</i> its config, and the calculator, which is typed into — get
/// it through <see cref="IWidgetSurfaceAware"/> rather than through a static hook or a widened
/// <see cref="IWidgetView.Attach"/>, so nothing that does not need it has to know it exists.
/// </para>
/// </summary>
public interface IWidgetSurface
{
    /// <summary>The user's own title for this widget, or null to use the catalogue label.</summary>
    string? Label { get; }

    /// <summary>
    /// Persist the config this view has just changed. Must not re-run layout: a relayout would push
    /// the config back into the control the user is typing in.
    /// </summary>
    void RequestSave();

    /// <summary>
    /// Ask for, or give up, the keyboard.
    /// <para>
    /// Outside edit mode the surface carries <c>WS_EX_NOACTIVATE</c>, so it never becomes the
    /// foreground window and no keystroke reaches it — a text box on it would show a caret while the
    /// typing went to whatever app actually had focus. Asking here lifts that for as long as the
    /// widget is being typed into. The cost is inherent to Win32 focus and worth naming: while you
    /// type into a widget, the window you were in loses focus.
    /// </para>
    /// </summary>
    void RequestTextInput(bool wanted);
}

/// <summary>Implemented by the few views that edit their own config or take typing.</summary>
public interface IWidgetSurfaceAware
{
    void Bind(IWidgetSurface surface);
}

/// <summary>What every widget view can do. Kept tiny on purpose: the card owns chrome and input.</summary>
public interface IWidgetView
{
    /// <summary>Bind the view to the shared ticker and its initial config.</summary>
    void Attach(Ticker ticker, WidgetConfig config);

    /// <summary>Re-read a config the user just changed.</summary>
    void ApplyConfig(WidgetConfig config);

    /// <summary>Release timers and handles. Must be safe to call more than once.</summary>
    void Detach();
}

/// <summary>One entry in the widget picker.</summary>
/// <param name="Kind">The persisted discriminator.</param>
/// <param name="Label">Display name.</param>
/// <param name="Description">One line shown under the name in the picker.</param>
/// <param name="Glyph">Segoe Fluent Icons code point, so the picker matches the rest of Windows.</param>
/// <param name="DefaultWidthPx">[S-10] Default size in device-independent pixels, converted to a fraction at drop time.</param>
internal readonly record struct WidgetDefinition(
    WidgetKind Kind,
    string Label,
    string Description,
    string Glyph,
    double DefaultWidthPx,
    double DefaultHeightPx);

/// <summary>
/// The widget registry. One place that maps a <see cref="WidgetKind"/> to its metadata and its view,
/// so adding a widget is one entry here plus one enum member — there is no second list to forget.
/// </summary>
internal static class WidgetCatalog
{
    public static readonly IReadOnlyList<WidgetDefinition> All =
    [
        new(WidgetKind.Clock, "Clock", "Time and date, digital or analog", Glyphs.Clock, 280, 140),
        new(WidgetKind.Cpu, "CPU", "Processor load across all cores", Glyphs.Cpu, 200, 150),
        new(WidgetKind.Memory, "Memory", "Physical memory in use", Glyphs.Memory, 200, 150),
        new(WidgetKind.Disk, "Disk", "Space used on a drive", Glyphs.Disk, 200, 150),
        new(WidgetKind.Battery, "Battery", "Charge level and time remaining", Glyphs.Battery, 200, 150),
        new(WidgetKind.Network, "Network", "Bytes per second across the active adapters", Glyphs.Network, 200, 150),
        new(WidgetKind.Ping, "Ping", "Round-trip time to a host", Glyphs.Ping, 200, 150),
        new(WidgetKind.Processes, "Processes", "The busiest processes, ranked", Glyphs.Processes, 320, 240),
        new(WidgetKind.Email, "Mail", "Recent messages, via the hearth hub", Glyphs.Mail, 380, 260),
        new(WidgetKind.Calendar, "Calendar", "What is coming up, via the hearth hub", Glyphs.Calendar, 340, 260),
        new(WidgetKind.Tasks, "Tasks", "Open tasks, via the hearth hub", Glyphs.Task, 320, 240),
        new(WidgetKind.Contacts, "Contacts", "People, via the hearth hub", Glyphs.Contact, 320, 260),
        new(WidgetKind.Notes, "Notes", "A note kept on this machine", Glyphs.Note, 300, 220),
        new(WidgetKind.Countdown, "Countdown", "Time left until the dates you care about", Glyphs.Countdown, 300, 200),
        new(WidgetKind.Pomodoro, "Pomodoro", "A focus and break timer", Glyphs.Pomodoro, 240, 200),
        new(WidgetKind.Calculator, "Calculator", "An arithmetic scratchpad", Glyphs.Calculator, 260, 260),
        new(WidgetKind.SunMoon, "Sun & Moon", "Sunrise, sunset and the moon phase", Glyphs.Sun, 320, 240),
        new(WidgetKind.Folder, "Folder", "What is in a folder on this PC", Glyphs.Folder, 340, 280),
    ];

    public static WidgetDefinition Definition(WidgetKind kind)
    {
        foreach (var definition in All)
        {
            if (definition.Kind == kind)
            {
                return definition;
            }
        }

        // Unreachable for a validated state, but a total function beats an exception on a render path.
        return All[0];
    }

    /// <summary>Build the view for a kind. Returns a <see cref="FrameworkElement"/> that also implements <see cref="IWidgetView"/>.</summary>
    public static FrameworkElement CreateView(WidgetKind kind) => kind switch
    {
        WidgetKind.Clock => new ClockView(),
        WidgetKind.Cpu or WidgetKind.Memory or WidgetKind.Disk
            or WidgetKind.Battery or WidgetKind.Network or WidgetKind.Ping => new MeterView(kind),
        WidgetKind.Processes => new ProcessesView(),
        WidgetKind.Email => new EmailView(),
        WidgetKind.Contacts => new ContactsView(),
        WidgetKind.Calendar => new CalendarView(),
        WidgetKind.Tasks => new TasksView(),
        WidgetKind.Notes => new NotesView(),
        WidgetKind.Countdown => new CountdownView(),
        WidgetKind.Pomodoro => new PomodoroView(),
        WidgetKind.Calculator => new CalculatorView(),
        WidgetKind.SunMoon => new SunMoonView(),
        WidgetKind.Folder => new FolderView(),
        _ => new ClockView(),
    };
}
