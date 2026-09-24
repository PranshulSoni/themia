namespace Themia.App.Shell;

/// <summary>
/// The Segoe Fluent Icons code points this app uses, by name.
/// <para>
/// Written as code points rather than as literal characters, because a private-use character sitting
/// in a source file is invisible: you cannot read it, diff it usefully, or spot a wrong one in
/// review. A named constant with its hex value is greppable and reviewable, and the name says what
/// the icon is for rather than what it looks like.
/// </para>
/// </summary>
internal static class Glyphs
{
    public static readonly string Settings = From(0xE713);
    public static readonly string Palette = From(0xE790);
    public static readonly string Grid = From(0xE80A);
    public static readonly string Info = From(0xE946);
    public static readonly string View = From(0xE7C4);
    public static readonly string Display = From(0xE7F4);
    public static readonly string Add = From(0xE710);
    public static readonly string Delete = From(0xE74D);
    public static readonly string Edit = From(0xE70F);
    public static readonly string Done = From(0xE73E);
    public static readonly string Copy = From(0xE8C8);
    public static readonly string Cut = From(0xE8C6);
    public static readonly string Paste = From(0xE77F);
    public static readonly string Reset = From(0xE777);
    public static readonly string Rename = From(0xE8AC);
    public static readonly string Theme = From(0xE793);
    public static readonly string Opacity = From(0xE7E6);
    public static readonly string Surface = From(0xE8B9);
    public static readonly string Font = From(0xE8D2);
    public static readonly string TextSize = From(0xE8E9);
    public static readonly string Scale = From(0xE8A9);
    public static readonly string Motion = From(0xE9D2);
    public static readonly string Clock = From(0xE823);
    public static readonly string Timer = From(0xE916);
    public static readonly string Hours = From(0xE917);
    public static readonly string Calendar = From(0xE787);
    public static readonly string Mail = From(0xE715);
    public static readonly string Unread = From(0xE8BD);
    public static readonly string Count = From(0xE8FD);
    public static readonly string Preview = From(0xE7C3);
    public static readonly string Startup = From(0xE7E8);
    public static readonly string Cpu = From(0xE950);
    public static readonly string Memory = From(0xE964);
    public static readonly string Disk = From(0xEDA2);
    public static readonly string Battery = From(0xE83F);
    public static readonly string Processes = From(0xE9D9);
    public static readonly string Network = From(0xE968);
    public static readonly string Volume = From(0xE767);
    public static readonly string Gpu = From(0xF211);
    public static readonly string Contact = From(0xE77B);
    public static readonly string Task = From(0xE9D5);
    public static readonly string Ping = From(0xEBC3);
    public static readonly string Note = From(0xE70B);
    public static readonly string Countdown = From(0xE81C);
    public static readonly string Pomodoro = From(0xE945);
    public static readonly string Calculator = From(0xE8EF);
    public static readonly string Sun = From(0xE706);
    public static readonly string Moon = From(0xE708);
    public static readonly string Place = From(0xE707);
    public static readonly string Sort = From(0xE8CB);
    public static readonly string Play = From(0xE768);
    public static readonly string Pause = From(0xE769);
    public static readonly string Stop = From(0xE71A);
    public static readonly string Sound = From(0xE767);
    public static readonly string Link = From(0xE71B);
    public static readonly string Wrap = From(0xE7A3);
    public static readonly string Clear = From(0xE894);

    private static string From(int codePoint) => char.ConvertFromUtf32(codePoint);
}
