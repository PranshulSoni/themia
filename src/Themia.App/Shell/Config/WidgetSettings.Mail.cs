using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.App.Widgets;
using Themia.Core.Accounts;
using Themia.Core.Mail;
using Themia.Core.Model;

namespace Themia.App.Shell.Config;

/// <summary>
/// [S-541] The Mail widget's settings: which of Themia's accounts it reads, the folder, how many
/// messages and in which order, and how each one is drawn. The classic widget's Source and Display
/// tabs, on one page.
/// <para>
/// Accounts themselves are signed in on the Accounts page, not here: this page lists what is
/// already signed in and links there, so there is exactly one place an account comes into being.
/// </para>
/// </summary>
internal static partial class WidgetSettings
{
    public static void Email(List<FrameworkElement> rows, EmailConfig email, Action save, Action<ConfigTarget> navigate)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(navigate);

        void OpenAccounts() => navigate(new ConfigTarget.App(AppPage.Accounts));

        rows.Add(FluentLayout.Header("Accounts"));
        MailAccountRows(rows, email, save, OpenAccounts);

        rows.Add(FluentLayout.Header("Messages"));

        rows.Add(FluentLayout.Row(Glyphs.Folder, "Folder", "Which folder of each account to list",
            FluentLayout.Choice<MailFolder>(
                [.. MailFolders.Listable.Select(f => (f, f.Label()))],
                email.Folder,
                value => { email.Folder = value; save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Count, "Messages",
            "At least the newest this many, across every account; a bigger widget lists more to fill its space",
            FluentLayout.Slider(email.EffectiveMaxCount, 1, 50, 1, string.Empty,
                value => { email.MaxCount = (int)Math.Round(value); save(); })));

        rows.Add(FluentLayout.Row(Glyphs.Sort, "Order", null,
            FluentLayout.Choice<MailSortOrder>(
                [(MailSortOrder.NewestFirst, "Newest first"), (MailSortOrder.OldestFirst, "Oldest first")],
                email.SortOrder,
                value => { email.SortOrder = value; save(); })));

        rows.Add(FluentLayout.Header("Display"));

        // The rows that only mean something in one view come and go with it.
        var listOnly = new List<FrameworkElement>();
        var previewOnly = new List<FrameworkElement>();
        var rowOnly = new List<FrameworkElement>();

        void ShowForView()
        {
            var row = email.ViewMode == MailViewMode.Row;
            var previewLines = email.ViewMode == MailViewMode.List && email.EffectiveListFields.Contains(MailListField.Preview);

            foreach (var element in listOnly)
            {
                element.Visibility = row ? Visibility.Collapsed : Visibility.Visible;
            }

            foreach (var element in previewOnly)
            {
                element.Visibility = previewLines ? Visibility.Visible : Visibility.Collapsed;
            }

            foreach (var element in rowOnly)
            {
                element.Visibility = row ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        rows.Add(FluentLayout.Row(Glyphs.View, "View",
            "List: three lines a message. Compact: two. Row: one line of columns",
            FluentLayout.Choice<MailViewMode>(
                [(MailViewMode.List, "List"), (MailViewMode.Compact, "Compact"), (MailViewMode.Row, "Row")],
                email.ViewMode,
                value => { email.ViewMode = value; save(); ShowForView(); })));

        // [S-543] The optional lines of the list and compact views. Sender and time always show.
        foreach (var (field, glyph, title, description) in new[]
        {
            (MailListField.Subject, Glyphs.Mail, "Show subject", (string?)null),
            (MailListField.Preview, Glyphs.Preview, "Show preview", "The start of the message, in the list view"),
            (MailListField.Account, Glyphs.Contact, "Show account", "Which account a message came to"),
        })
        {
            var toggle = FluentLayout.Toggle(email.EffectiveListFields.Contains(field), on =>
            {
                var fields = email.EffectiveListFields.ToList();
                fields.Remove(field);
                if (on)
                {
                    fields.Add(field);
                }

                // Kept in the canonical order, so the file reads the same however it was toggled.
                email.ListFields = [.. EmailConfig.DefaultListFields.Where(fields.Contains)];
                save();
                ShowForView();
            });

            var row = FluentLayout.Row(glyph, title, description, toggle);
            listOnly.Add(row);
            rows.Add(row);
        }

        var lines = FluentLayout.Row(Glyphs.Wrap, "Preview lines", "How many lines of each message's preview",
            FluentLayout.Slider(email.EffectivePreviewLines, 1, 5, 1, string.Empty,
                value => { email.PreviewLines = (int)Math.Round(value); save(); }));
        previewOnly.Add(lines);
        rows.Add(lines);

        var columnsHeader = FluentLayout.Note("Columns, in order. A narrow widget drops the preview, account and date columns first.");
        rowOnly.Add(columnsHeader);
        rows.Add(columnsHeader);

        var columns = new StackPanel { Spacing = Fluent.Space1 };
        rowOnly.Add(columns);
        rows.Add(columns);
        MailColumnRows(columns, email, save);

        ShowForView();
    }

    /// <summary>
    /// "Every mail account", or a choice of them. Every is the default and means accounts added later
    /// join too; turning it off starts from the accounts there are now, so nothing disappears.
    /// </summary>
    private static void MailAccountRows(List<FrameworkElement> rows, EmailConfig email, Action save, Action openAccounts)
    {
        var accounts = AppServices.Accounts?.WithFeature(AccountFeatures.Mail) ?? [];

        var manage = new Button { Content = accounts.Count == 0 ? "Add an account" : "Manage accounts" };
        manage.Click += (_, _) => openAccounts();

        if (accounts.Count == 0)
        {
            rows.Add(FluentLayout.Row(Glyphs.Mail, "No mail account yet",
                "Sign in to Microsoft, Google or a mail server (IMAP) on the Accounts page", manage));
            return;
        }

        var toggles = new List<ToggleSwitch>();
        var chosenRows = new List<FrameworkElement>();

        // Set while this page moves a switch itself, so the switch's own handler does not take that
        // for the user choosing one account.
        var quiet = false;
        var every = email.EffectiveAccountIds.Count == 0;

        void ShowChoice()
        {
            foreach (var row in chosenRows)
            {
                row.Visibility = email.EffectiveAccountIds.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        rows.Add(FluentLayout.Row(Glyphs.Mail, "Every mail account", "Accounts you add later are included too",
            FluentLayout.Toggle(every, on =>
            {
                // Off starts from every account there is now, so turning it off changes nothing on
                // screen until an account is then turned off.
                email.AccountIds = on ? [] : [.. accounts.Select(a => a.Id)];

                quiet = true;
                foreach (var toggle in toggles)
                {
                    toggle.IsOn = true;
                }

                quiet = false;

                save();
                ShowChoice();
            })));

        foreach (var account in accounts)
        {
            var index = toggles.Count;
            var toggle = FluentLayout.Toggle(every || email.EffectiveAccountIds.Contains(account.Id), on =>
            {
                if (quiet)
                {
                    return;
                }

                var ids = email.EffectiveAccountIds.ToList();
                ids.Remove(account.Id);
                if (on)
                {
                    ids.Add(account.Id);
                }

                // At least one stays: an empty list means "every account", the opposite of the
                // user turning the last one off.
                if (ids.Count == 0)
                {
                    quiet = true;
                    toggles[index].IsOn = true;
                    quiet = false;
                    return;
                }

                email.AccountIds = [.. accounts.Select(a => a.Id).Where(ids.Contains)];
                save();
            });

            toggles.Add(toggle);

            var description = account.Problem ?? $"{account.Provider.Label()} · {account.Address}";
            var row = FluentLayout.Row(EmailView.FolderGlyph(MailFolder.Inbox), account.Label, description, toggle);
            chosenRows.Add(row);
            rows.Add(row);
        }

        rows.Add(FluentLayout.Row(Glyphs.Contact, "Accounts", "Sign in, sign out and rename on the Accounts page", manage));
        ShowChoice();
    }

    /// <summary>[S-543] The row view's columns: on or off, and in which order. One always stays on.</summary>
    private static void MailColumnRows(StackPanel host, EmailConfig email, Action save)
    {
        void Rebuild()
        {
            host.Children.Clear();

            var chosen = email.EffectiveColumns.ToList();
            var all = chosen.Concat(Enum.GetValues<MailColumn>().Where(c => !chosen.Contains(c))).ToList();

            for (var i = 0; i < all.Count; i++)
            {
                var column = all[i];
                var on = chosen.Contains(column);
                var index = chosen.IndexOf(column);

                var toggle = FluentLayout.Toggle(on, value =>
                {
                    var next = email.EffectiveColumns.ToList();
                    if (value)
                    {
                        next.Add(column);
                    }
                    else if (next.Count > 1)
                    {
                        next.Remove(column);
                    }

                    email.RowColumns = next;
                    save();
                    Rebuild();
                });

                var up = FluentLayout.IconCommand(Glyphs.MoveUp, "Move up");
                up.IsEnabled = on && index > 0;
                up.Click += (_, _) => Move(-1);

                var down = FluentLayout.IconCommand(Glyphs.MoveDown, "Move down");
                down.IsEnabled = on && index >= 0 && index < chosen.Count - 1;
                down.Click += (_, _) => Move(+1);

                void Move(int by)
                {
                    var next = email.EffectiveColumns.ToList();
                    var at = next.IndexOf(column);
                    if (at < 0 || at + by < 0 || at + by >= next.Count)
                    {
                        return;
                    }

                    (next[at], next[at + by]) = (next[at + by], next[at]);
                    email.RowColumns = next;
                    save();
                    Rebuild();
                }

                var controls = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = Fluent.Space1,
                    Children = { up, down, toggle },
                };

                host.Children.Add(FluentLayout.Row(ColumnGlyph(column), column.Label(), null, controls));
            }
        }

        Rebuild();
    }

    private static string ColumnGlyph(MailColumn column) => column switch
    {
        MailColumn.From => Glyphs.Contact,
        MailColumn.Subject => Glyphs.Mail,
        MailColumn.Date => Glyphs.Calendar,
        MailColumn.Preview => Glyphs.Preview,
        _ => Glyphs.Contact,
    };
}
