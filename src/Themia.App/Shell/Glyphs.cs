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
    public static readonly string MoveUp = From(0xE74A);
    public static readonly string MoveDown = From(0xE74B);
    public static readonly string Folder = From(0xE8B7);
    public static readonly string File = From(0xE8A5);
    public static readonly string Hidden = From(0xE890);
    public static readonly string Open = From(0xE8E5);
    public static readonly string Power = From(0xE7E8);
    public static readonly string Shortcut = From(0xE71B);
    public static readonly string Warning = From(0xE7BA);
    public static readonly string Undo = From(0xE7A7);
    public static readonly string Eraser = From(0xED60);
    public static readonly string Shuffle = From(0xE8B1);
    public static readonly string Mute = From(0xE74F);
    public static readonly string Volume0 = From(0xE992);
    public static readonly string Volume1 = From(0xE993);
    public static readonly string Volume2 = From(0xE994);
    public static readonly string Volume3 = From(0xE995);
    public static readonly string Previous = From(0xE892);
    public static readonly string Next = From(0xE893);
    public static readonly string Headphone = From(0xE7F6);
    public static readonly string Mouse = From(0xE962);
    public static readonly string Keyboard = From(0xE765);
    public static readonly string Phone = From(0xE8EA);
    public static readonly string Game = From(0xE7FC);
    public static readonly string Pen = From(0xE929);
    public static readonly string Lightning = From(0xE945);
    public static readonly string BatterySaver = From(0xEBC0);
    public static readonly string Bluetooth = From(0xE702);
    public static readonly string Weather = From(0xE753);
    public static readonly string Forecast = From(0xE9CA);

    // The weather set. These code points are the least certain in this file: a wrong one renders
    // as a hollow box rather than failing, so they are checked against a screenshot rather than
    // trusted.
    public static readonly string Cloudy = From(0xE9BF);
    public static readonly string Fog = From(0xE9CB);
    public static readonly string Rain = From(0xE9C4);
    public static readonly string Snow = From(0xE9C8);
    public static readonly string Thunderstorm = From(0xE9C6);
    public static readonly string Stocks = From(0xEB05);
    public static readonly string Rss = From(0xE8F1);
    public static readonly string Apps = From(0xE71D);
    public static readonly string Search = From(0xE721);
    public static readonly string Clipboard = From(0xE77F);
    public static readonly string Scratchpad = From(0xE70F);
    public static readonly string Color = From(0xE790);
    public static readonly string Quote = From(0xE9E5);
    public static readonly string Gallery = From(0xE91B);
    public static readonly string Habit = From(0xE73E);
    public static readonly string Mood = From(0xE76E);
    public static readonly string Music = From(0xE8D6);
    public static readonly string Group = From(0xF168);
    public static readonly string Stack = From(0xE81E);
    public static readonly string Tabs = From(0xE737);

    private static string From(int codePoint) => char.ConvertFromUtf32(codePoint);
}
