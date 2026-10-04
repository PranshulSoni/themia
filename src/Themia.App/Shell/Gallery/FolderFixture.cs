#if DEBUG
using System.Diagnostics;
using System.Text;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Shell.Gallery;

/// <summary>
/// [S-1339] <c>Folder+fixture</c>: a made-up project folder under the gallery's own temp directory —
/// sub-folders, documents, a few pictures (so the grid views show thumbnails) and, when git is on
/// the PATH, a repository with untracked and modified files for the badge and the Git column. Never
/// the user's own files. Fixed timestamps, so every render reads the same.
/// <para>
/// <c>THEMIA_GALLERY_FOLDER_VIEW</c> = List / Details / Grid / Icons picks the view (default Details),
/// so each view's ladder can be rendered.
/// </para>
/// </summary>
internal static class FolderFixture
{
    private const string Scope = "gallery";

    public static string Root => Path.Combine(Path.GetTempPath(), "Themia.Gallery", "Folder", "Northwind launch");

    public static FolderConfig Config()
    {
        Build();

        var view = Enum.TryParse<FolderViewMode>(Environment.GetEnvironmentVariable("THEMIA_GALLERY_FOLDER_VIEW"), true, out var v)
            ? v
            : FolderViewMode.Details;

        return new FolderConfig
        {
            Path = Root,
            View = view,
            Navigation = FolderNavigation.Navigate,
            Columns = [FolderColumn.Size, FolderColumn.Modified, FolderColumn.Type, FolderColumn.Git, FolderColumn.ItemCount],
        };
    }

    private static void Build()
    {
        try
        {
            var stamp = new DateTime(2026, 9, 28, 14, 30, 0, DateTimeKind.Local);
            Directory.CreateDirectory(Root);

            string[] folders = ["Brand assets", "Budget", "Meeting notes", "Press kit", "Research"];

            for (var i = 0; i < folders.Length; i++)
            {
                var folder = Path.Combine(Root, folders[i]);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "readme.txt"), "Fixture file.");
                Directory.SetLastWriteTime(folder, stamp.AddDays(-i * 3));
            }

            (string Name, int Bytes, int DaysAgo)[] files =
            [
                ("Launch plan.docx", 48_200, 0),
                ("Budget 2027.xlsx", 23_900, 1),
                ("Keynote.pptx", 2_400_000, 2),
                ("Notes.md", 3_100, 4),
                ("Contract draft.pdf", 410_000, 9),
                ("Checklist.txt", 900, 15),
                ("Timeline.csv", 7_200, 40),
                ("Logo.svg", 5_600, 120),
                ("setup.ps1", 1_200, 400),
            ];

            foreach (var (name, bytes, days) in files)
            {
                var path = Path.Combine(Root, name);

                if (!File.Exists(path))
                {
                    File.WriteAllBytes(path, new byte[bytes]);
                }

                File.SetLastWriteTime(path, stamp.AddDays(-days));
            }

            (string Name, byte R, byte G, byte B)[] pictures =
            [
                ("Hero shot.bmp", 0x2F, 0x6F, 0xD8),
                ("Team photo.bmp", 0xE0, 0x8A, 0x2C),
                ("Venue.bmp", 0x3C, 0xA8, 0x6B),
            ];

            foreach (var (name, r, g, b) in pictures)
            {
                var path = Path.Combine(Root, name);

                if (!File.Exists(path))
                {
                    File.WriteAllBytes(path, Bitmap(96, 72, r, g, b));
                }

                File.SetLastWriteTime(path, stamp.AddDays(-3));
            }

            if (!Directory.Exists(Path.Combine(Root, ".git")))
            {
                Git("init", "-q", "-b", "main");
                Git("add", "Launch plan.docx", "Notes.md", "Checklist.txt");
                Git("-c", "user.name=Gallery", "-c", "user.email=gallery@example.invalid", "commit", "-q", "-m", "Fixture");
            }

            // A modified tracked file, every run, for the "~" count.
            File.WriteAllText(Path.Combine(Root, "Notes.md"), $"# Notes{Environment.NewLine}Edited.");
            File.SetLastWriteTime(Path.Combine(Root, "Notes.md"), stamp.AddDays(-4));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(Scope, "the folder fixture could not be built", ex);
        }
    }

    private static void Git(params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };

            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            process?.StandardOutput.ReadToEnd();
            process?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // ignore: no git on this machine; the fixture simply has no repository.
            Log.Debug(Scope, $"git unavailable for the folder fixture: {ex.Message}");
        }
    }

    /// <summary>A 24-bit BMP with a diagonal gradient of one colour: a picture with a real thumbnail.</summary>
    private static byte[] Bitmap(int width, int height, byte r, byte g, byte b)
    {
        var stride = ((width * 3) + 3) & ~3;
        var size = 54 + (stride * height);
        var bytes = new byte[size];

        Encoding.ASCII.GetBytes("BM").CopyTo(bytes, 0);
        BitConverter.GetBytes(size).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(width).CopyTo(bytes, 18);
        BitConverter.GetBytes(height).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var shade = 0.55 + (0.45 * (x + y) / (width + height));
                var at = 54 + (y * stride) + (x * 3);
                bytes[at] = (byte)(b * shade);
                bytes[at + 1] = (byte)(g * shade);
                bytes[at + 2] = (byte)(r * shade);
            }
        }

        return bytes;
    }
}
#endif
