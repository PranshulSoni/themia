using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Themia.App.Services;
using Themia.Core.Diagnostics;
using Themia.Core.Localization;
using Themia.Core.Widgets;

namespace Themia.App.Shell;

/// <summary>
/// [S-827] / [S-1723] The one font picker: the four presets, then every installed family, each
/// entry drawn in the font it names, so the list is its own preview. Every font setting uses this
/// control rather than a list of names.
/// <para>
/// The installed list arrives off the UI thread; until it does the current family is listed on
/// its own so the choice shows what is set.
/// </para>
/// </summary>
internal static class FontChoice
{
    private const string Scope = "font-choice";

    /// <summary>
    /// A drop-down holding <paramref name="current"/> (a preset or a family name). <paramref name="onChanged"/>
    /// gets the chosen value when the user picks another one; never while the list is being filled.
    /// </summary>
    public static ComboBox Create(string current, Action<string> onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        var combo = new ComboBox { MinWidth = FluentLayout.ChoiceMinWidth, MaxDropDownHeight = 420 };
        var filling = true;

        void Add(string family, string label) => combo.Items.Add(Item(family, label));

        Add(DesignLook.SystemFont, Str.Shell_Appearance_SystemFont.Text());
        Add(DesignLook.SansFont, Str.Shell_Appearance_SansFont.Text());
        Add(DesignLook.SerifFont, Str.Settings_Font_Serif.Text());
        Add(DesignLook.MonoFont, Str.Settings_Font_Mono.Text());

        var listedOwn = !DesignLook.FontPresets.Contains(current, StringComparer.OrdinalIgnoreCase) && DesignLook.IsFamilyName(current);
        if (listedOwn)
        {
            Add(current, current);
        }

        void Select()
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem { Tag: string family } && family.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            combo.SelectedIndex = 0;
        }

        Select();
        filling = false;

        combo.SelectionChanged += (_, _) =>
        {
            if (!filling && combo.SelectedItem is ComboBoxItem { Tag: string family })
            {
                current = family;
                onChanged(family);
            }
        };

        var queue = DispatcherQueue.GetForCurrentThread();
        var own = current;
        _ = SystemFonts.LoadAsync().ContinueWith(
            task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    Log.Warn(Scope, "the installed font list could not be read; only the presets are offered", task.Exception);
                    return;
                }

                if (queue is null || !queue.TryEnqueue(() =>
                {
                    filling = true;
                    try
                    {
                        foreach (var family in task.Result)
                        {
                            // The family already listed on its own is not listed twice.
                            if (!(listedOwn && family.Equals(own, StringComparison.OrdinalIgnoreCase)))
                            {
                                Add(family, family);
                            }
                        }

                        Select();
                    }
                    finally
                    {
                        filling = false;
                    }
                }))
                {
                    Log.Warn(Scope, "could not list the installed fonts on the settings thread");
                }
            },
            TaskScheduler.Default);

        return combo;
    }

    /// <summary>
    /// One entry, in its own font: a preset in the family it maps to ([S-826]), the system entry in
    /// the UI's own. The name stays plain text, so the closed box, search and a screen reader read it.
    /// </summary>
    private static ComboBoxItem Item(string family, string label)
    {
        var item = new ComboBoxItem { Content = label, Tag = family };

        if (DesignLook.FontFamilyName(family) is { } name)
        {
            item.FontFamily = new FontFamily(name);
        }

        return item;
    }
}
