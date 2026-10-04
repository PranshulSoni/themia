using System.Text.Json;
using Themia.Core.Storage;
using Themia.Core.Tasks;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// Lists kept on this PC, ported from the classic app's <c>localLists.test.ts</c> and
/// <c>localTasks.test.ts</c>, plus the file they live in.
/// </summary>
public sealed class LocalTaskListsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "themia-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // ignore: temp cleanup is best effort and must not fail a test run
        }
    }

    private static LocalTask T(string id, string? title = null, bool done = false) => new(id, title ?? id, done);

    private static LocalTaskList L(string id, string name, params LocalTask[] tasks) => new(id, name, tasks);

    private static IReadOnlyList<LocalTaskList> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var dropped = 0;
        return LocalTaskLists.Parse(document.RootElement, ref dropped);
    }

    // ---- [S-603] the trust boundary --------------------------------------------------------------

    [Theory]
    [InlineData("null")]
    [InlineData("\"nope\"")]
    [InlineData("{\"lists\":[]}")]
    public void S603_a_non_array_is_no_lists(string json) => Assert.Empty(Parse(json));

    [Fact]
    public void S603_round_trips_well_formed_lists()
    {
        var lists = Parse("""[{"id":"l1","name":"Work","tasks":[{"id":"a","title":"Ship it","done":true}]},{"id":"l2","name":"Home","tasks":[]}]""");

        Assert.Equal(["l1", "l2"], lists.Select(l => l.Id));
        Assert.Equal(T("a", "Ship it", true), Assert.Single(lists[0].Tasks));
        Assert.Empty(lists[1].Tasks);
    }

    [Fact]
    public void S603_drops_an_entry_without_a_usable_id_or_name_keeping_its_siblings()
    {
        var lists = Parse("""[{"id":"l1","name":"Keep"},{"name":"no id"},{"id":"","name":"empty"},{"id":7,"name":"numeric"},{"id":"l2"},null,"nope",{"id":"l3","name":"Keep too"}]""");
        Assert.Equal(["l1", "l3"], lists.Select(l => l.Id));
    }

    [Fact]
    public void S603_a_corrupt_task_array_empties_the_list_rather_than_dropping_it()
    {
        var list = Assert.Single(Parse("""[{"id":"l1","name":"Work","tasks":"garbage"}]"""));
        Assert.Empty(list.Tasks);
    }

    [Fact]
    public void S603_a_repeated_id_is_dropped_keeping_the_first()
    {
        var list = Assert.Single(Parse("""[{"id":"dup","name":"Original","tasks":[{"id":"a","title":"Mine"}]},{"id":"dup","name":"Impostor"}]"""));
        Assert.Equal("Original", list.Name);
    }

    [Fact]
    public void S603_tasks_default_a_non_boolean_done_and_drop_malformed_entries()
    {
        var list = Assert.Single(Parse("""[{"id":"l","name":"L","tasks":[{"id":"a","title":"no done"},{"id":"b","title":"string done","done":"yes"},{"id":123,"title":"numeric"},{"id":"x"},{"title":"no id"},null,{"id":"c","title":"T","done":true,"junk":1}]}]"""));
        Assert.Equal([T("a", "no done"), T("b", "string done"), T("c", "T", true)], list.Tasks);
    }

    // ---- [S-604] lists -----------------------------------------------------------------------------

    [Fact]
    public void S604_an_unknown_or_absent_id_finds_nothing_never_another_list()
    {
        IReadOnlyList<LocalTaskList> lists = [L("l1", "Work"), L("l2", "Home")];
        Assert.Equal("Home", LocalTaskLists.Find(lists, "l2")!.Name);
        Assert.Null(LocalTaskLists.Find(lists, "gone"));
        Assert.Null(LocalTaskLists.Find(lists, string.Empty));
        Assert.Null(LocalTaskLists.Find(lists, null));
    }

    [Fact]
    public void S604_add_trims_and_never_leaves_a_list_unnamed()
    {
        Assert.Equal("Groceries", LocalTaskLists.Add([], "l1", "  Groceries  ", "Untitled")[0].Name);
        Assert.Equal("Untitled", LocalTaskLists.Add([], "l1", "   ", "Untitled")[0].Name);
    }

    [Fact]
    public void S604_rename_keeps_a_blank_name_so_the_box_can_be_cleared()
        => Assert.Equal(string.Empty, LocalTaskLists.Rename([L("l1", "Work")], "l1", string.Empty)[0].Name);

    [Fact]
    public void S604_remove_takes_the_tasks_with_it_and_an_unknown_id_is_a_no_op()
    {
        IReadOnlyList<LocalTaskList> lists = [L("l1", "Work", T("a")), L("l2", "Home")];
        Assert.Equal(["l2"], LocalTaskLists.Remove(lists, "l1").Select(l => l.Id));
        Assert.Equal(["l1", "l2"], LocalTaskLists.Remove(lists, "nope").Select(l => l.Id));
    }

    [Fact]
    public void S603_a_write_to_a_list_that_is_gone_is_dropped_not_a_resurrection()
    {
        IReadOnlyList<LocalTaskList> lists = [L("l1", "Work")];
        var after = LocalTaskLists.SetTasks(lists, "deleted", [T("a")]);
        Assert.Equal(["l1"], after.Select(l => l.Id));
        Assert.Empty(after[0].Tasks);
    }

    [Fact]
    public void S604_unique_names_number_collisions_and_fill_holes()
    {
        IReadOnlyList<LocalTaskList> one = [L("l1", "New list")];
        Assert.Equal("New list", LocalTaskLists.UniqueName([], "New list"));
        Assert.Equal("New list 2", LocalTaskLists.UniqueName(one, "New list"));
        Assert.Equal("New list 3", LocalTaskLists.UniqueName([.. one, L("l2", "New list 2")], "New list"));
        Assert.Equal("New list 2", LocalTaskLists.UniqueName([.. one, L("l2", "New list 3")], "New list"));
    }

    [Fact]
    public void S604_switching_to_this_PC_prefers_the_old_list_then_the_first_and_creates_only_when_empty()
    {
        IReadOnlyList<LocalTaskList> lists = [L("l1", "Work"), L("l2", "Home")];
        Assert.Equal("l2", LocalTaskLists.Ensure(lists, "l2"));
        Assert.Equal("l1", LocalTaskLists.Ensure(lists, "gone"));
        Assert.Null(LocalTaskLists.Ensure([], "gone"));
    }

    // ---- tasks ---------------------------------------------------------------------------------------

    [Fact]
    public void S599_adding_appends_at_the_bottom_trimmed_and_a_blank_title_is_a_no_op()
    {
        IReadOnlyList<LocalTask> tasks = [T("a")];
        Assert.Equal([T("a"), T("n", "New task")], LocalTaskLists.AddTask(tasks, "  New task  ", "n"));
        Assert.Same(tasks, LocalTaskLists.AddTask(tasks, "   ", "x"));
    }

    [Fact]
    public void S603_toggle_flips_one_task_and_leaves_the_input_alone()
    {
        IReadOnlyList<LocalTask> tasks = [T("a"), T("b", done: true)];
        Assert.Equal([T("a", done: true), T("b", done: true)], LocalTaskLists.Toggle(tasks, "a"));
        Assert.Equal(tasks, LocalTaskLists.Toggle(tasks, "zzz"));
        Assert.False(tasks[0].Done);
    }

    [Fact]
    public void S600_rename_trims_keeps_done_and_ignores_a_blank_title()
    {
        IReadOnlyList<LocalTask> tasks = [T("a"), T("b", "B", true)];
        Assert.Equal([T("a", "Renamed"), T("b", "B", true)], LocalTaskLists.RenameTask(tasks, "a", "  Renamed  "));
        Assert.Equal([T("a"), T("b", "Still done", true)], LocalTaskLists.RenameTask(tasks, "b", "Still done"));
        Assert.Same(tasks, LocalTaskLists.RenameTask(tasks, "a", "   "));
    }

    [Fact]
    public void S603_remove_drops_one_task()
        => Assert.Equal([T("b")], LocalTaskLists.RemoveTask([T("a"), T("b")], "a"));

    [Fact]
    public void S598_reorder_rewrites_the_stored_order()
        => Assert.Equal(["c", "a", "b"], LocalTaskLists.Reorder([T("a"), T("b"), T("c")], ["c", "a", "b"]).Select(t => t.Id));

    [Fact]
    public void S598_reorder_leaves_a_hidden_completed_task_in_its_own_slot()
        => Assert.Equal(["b", "done", "a"], LocalTaskLists.Reorder([T("a"), T("done", done: true), T("b")], ["b", "a"]).Select(t => t.Id));

    [Fact]
    public void S598_reorder_ignores_unknown_ids_and_keeps_every_task()
    {
        Assert.Equal(["c", "a", "b"], LocalTaskLists.Reorder([T("a"), T("b"), T("c")], ["ghost", "c", "a", "b"]).Select(t => t.Id));
        Assert.Equal(["a", "b", "c"], LocalTaskLists.Reorder([T("a"), T("b"), T("c")], ["c"]).Select(t => t.Id));
    }

    // ---- [S-603] the file --------------------------------------------------------------------------

    [Fact]
    public void S603_the_file_round_trips_and_carries_a_schema_version()
    {
        IReadOnlyList<LocalTaskList> lists = [L("l1", "Work \"quoted\"", T("a", "Ship it", true)), L("l2", "Home")];
        var json = LocalTaskStore.Serialize(lists);

        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);

        var (back, dropped, refusal) = LocalTaskStore.Parse(json);
        Assert.Null(refusal);
        Assert.Equal(0, dropped);
        Assert.Equal("Work \"quoted\"", back[0].Name);
        Assert.Equal(T("a", "Ship it", true), Assert.Single(back[0].Tasks));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{"lists":[]}""")]
    [InlineData("""{"schemaVersion":99,"lists":[]}""")]
    public void S603_a_document_that_cannot_be_trusted_is_refused_whole(string json)
        => Assert.NotNull(LocalTaskStore.Parse(json).Refusal);

    [Fact]
    public void S603_an_absent_file_is_a_quiet_empty_pool()
    {
        var load = new LocalTaskStore(Path.Combine(_dir, LocalTaskStore.FileName)).Load();
        Assert.Equal(LoadOutcome.Fresh, load.Outcome);
        Assert.Empty(load.Lists);
        Assert.True(load.Writable);
    }

    [Fact]
    public async Task S603_a_corrupt_file_is_set_aside_never_deleted_and_saving_is_atomic()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, LocalTaskStore.FileName);
        await File.WriteAllTextAsync(path, "{ definitely not json");

        var store = new LocalTaskStore(path);
        var load = store.Load();

        Assert.Equal(LoadOutcome.Recovered, load.Outcome);
        Assert.True(load.Writable);
        Assert.Single(Directory.GetFiles(_dir, "tasks.json.corrupt-*"));

        await store.SaveAsync([L("l1", "Work", T("a"))]);

        Assert.Equal(["l1"], store.Load().Lists.Select(l => l.Id));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
}
