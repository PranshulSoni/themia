using System.Runtime.InteropServices;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// [S-827] The font families installed on this PC, for the Appearance page's font picker — every
/// family, as the original listed (<c>list_system_fonts</c>), not a hand-picked six.
/// <para>
/// GDI's <c>EnumFontFamiliesExW</c> rather than DirectWrite: it is one flat call with a plain
/// callback, needs no COM vtable, and its family names are ones XAML's <c>FontFamily</c> resolves.
/// The list is read once, off the UI thread, and cached: fonts installed mid-session are rare
/// enough that reopening the app is a fair price, and Settings never re-enumerates on open.
/// </para>
/// </summary>
internal static unsafe partial class SystemFonts
{
    private const string Scope = "fonts";

    private const byte DefaultCharset = 1;

    private const int LfFaceSize = 32;

    private static Task<IReadOnlyList<string>>? _cache;

    /// <summary>The installed families, sorted and de-duplicated. Empty (logged) if GDI fails.</summary>
    public static Task<IReadOnlyList<string>> LoadAsync() => _cache ??= Task.Run(Enumerate);

    private static IReadOnlyList<string> Enumerate()
    {
        var names = new List<string>();
        var dc = GetDC(0);
        if (dc == 0)
        {
            Log.Warn(Scope, "no screen DC; the font list is only the presets");
            return [];
        }

        var handle = GCHandle.Alloc(names);
        try
        {
            var logFont = default(LogFontW);
            logFont.Charset = DefaultCharset;
            EnumFontFamiliesExW(dc, &logFont, &OnFamily, GCHandle.ToIntPtr(handle), 0);
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "font enumeration failed; the font list is only the presets", ex);
        }
        finally
        {
            handle.Free();
            _ = ReleaseDC(0, dc);
        }

        var list = DesignLook.FontList(names);
        Log.Info(Scope, $"{list.Count} installed font families");
        return list;
    }

    /// <summary>GDI's callback: one call per family and script. Must never let an exception out.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int OnFamily(LogFontW* font, void* metrics, uint type, nint state)
    {
        try
        {
            if (GCHandle.FromIntPtr(state).Target is List<string> names)
            {
                names.Add(new string(font->FaceName, 0, LfFaceSize).TrimEnd('\0').Split('\0')[0]);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, "a font family could not be read", ex);
        }

        return 1; // keep enumerating
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LogFontW
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte Charset;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;
        public fixed char FaceName[LfFaceSize];
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll", EntryPoint = "EnumFontFamiliesExW")]
    private static partial int EnumFontFamiliesExW(
        nint dc,
        LogFontW* logFont,
        delegate* unmanaged[Stdcall]<LogFontW*, void*, uint, nint, int> callback,
        nint state,
        uint flags);
}
