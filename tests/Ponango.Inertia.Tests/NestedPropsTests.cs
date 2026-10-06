using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// Nested prop types and dot-notation paths (Inertia v3 "Nested Prop Types").
/// </summary>
public class NestedPropsTests
{
    static object Dashboard(Func<int>? notifications = null) => new
    {
        auth = new
        {
            user = new { name = "Alice" },
            notifications = Inertia.Defer(() => notifications?.Invoke() ?? 3),
            invoices = Inertia.Optional(() => new[] { "inv-1" })
        }
    };

    [Fact]
    public async Task Nested_deferred_prop_is_announced_with_its_dot_path()
    {
        using var test = Inertia_();
        var calls = 0;

        using var document = await Render(test, Dashboard(() => ++calls));

        var auth = Props(document).GetProperty("auth");
        Assert.Equal("Alice", auth.GetProperty("user").GetProperty("name").GetString());
        Assert.False(auth.TryGetProperty("notifications", out _));
        Assert.False(auth.TryGetProperty("invoices", out _));
        Assert.Equal(0, calls);
        Assert.Equal(new[] { "auth.notifications" }, TestHelpers.Strings(document.RootElement.GetProperty("deferredProps").GetProperty("default")));
    }

    [Fact]
    public async Task Only_nested_path_resolves_that_leaf_and_its_ancestors()
    {
        using var test = Inertia_(only: "auth.notifications");

        using var document = await Render(test, Dashboard());

        var auth = Props(document).GetProperty("auth");
        Assert.Equal(3, auth.GetProperty("notifications").GetInt32());
        Assert.False(auth.TryGetProperty("user", out _));
        Assert.False(auth.TryGetProperty("invoices", out _));
        Assert.False(document.RootElement.TryGetProperty("deferredProps", out _));
    }

    [Fact]
    public async Task Except_nested_path_removes_only_that_leaf()
    {
        using var test = Inertia_(only: "auth", except: "auth.invoices");

        using var document = await Render(test, Dashboard());

        var auth = Props(document).GetProperty("auth");
        Assert.True(auth.TryGetProperty("user", out _));
        Assert.Equal(3, auth.GetProperty("notifications").GetInt32());
        Assert.False(auth.TryGetProperty("invoices", out _));
    }

    [Fact]
    public async Task Nested_merge_and_once_metadata_use_dot_paths()
    {
        using var test = Inertia_();

        using var document = await Render(test, new
        {
            dashboard = new
            {
                feed = Inertia.Merge(() => new { items = new[] { 1 } }).Append("items", matchOn: "id"),
                plans = Inertia.Once(() => new[] { "pro" }).Until(TimeSpan.FromHours(1))
            }
        });

        Assert.Equal(new[] { "dashboard.feed.items" }, TestHelpers.Strings(document.RootElement.GetProperty("mergeProps")));
        Assert.Equal(new[] { "dashboard.feed.items.id" }, TestHelpers.Strings(document.RootElement.GetProperty("matchPropsOn")));
        Assert.Equal("dashboard.plans", document.RootElement.GetProperty("onceProps").GetProperty("dashboard.plans").GetProperty("prop").GetString());
    }

    [Fact]
    public async Task Nested_once_prop_known_to_client_is_skipped()
    {
        using var test = Inertia_();
        test.HttpContext.Request.Headers["X-Inertia-Except-Once-Props"] = "dashboard.plans";

        using var document = await Render(test, new { dashboard = new { title = "Hi", plans = Inertia.Once(() => "pro") } });

        var dashboard = Props(document).GetProperty("dashboard");
        Assert.Equal("Hi", dashboard.GetProperty("title").GetString());
        Assert.False(dashboard.TryGetProperty("plans", out _));
        Assert.True(document.RootElement.GetProperty("onceProps").TryGetProperty("dashboard.plans", out _));
    }

    [Fact]
    public async Task Rebuilt_anonymous_objects_keep_naming_policy_and_null_skipping()
    {
        using var test = Inertia_();

        using var document = await Render(test, new
        {
            auth = new
            {
                UserName = "Alice",
                Avatar = (string?)null,
                Notifications = Inertia.Defer(() => 1)
            }
        });

        var auth = Props(document).GetProperty("auth");
        Assert.Equal("Alice", auth.GetProperty("userName").GetString());
        Assert.False(auth.TryGetProperty("avatar", out _));
        Assert.Equal(new[] { "auth.notifications" }, TestHelpers.Strings(document.RootElement.GetProperty("deferredProps").GetProperty("default")));
    }

    [Fact]
    public async Task Containers_without_prop_types_serialize_exactly_as_before()
    {
        using var test = Inertia_();
        var original = new
        {
            Title = "Report",
            Missing = (string?)null,
            Rows = new[] { new { Id = 1, Label = "a" } },
            Meta = new Dictionary<string, object?> { ["Total"] = 1, ["Empty"] = null },
            Poco = new Poco("x", null)
        };

        using var document = await Render(test, new { report = original });

        // The adapter's default serializer settings.
        var expected = JsonSerializer.Serialize(original, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
        });
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(Props(document).GetProperty("report").GetRawText())),
            Props(document).GetProperty("report").GetRawText());
    }

    [Fact]
    public async Task Top_level_dot_keys_are_unpacked_into_nested_objects()
    {
        using var test = Inertia_();

        using var document = await Render(test, new Dictionary<string, object>
        {
            ["auth.user.name"] = "Alice",
            ["auth.team"] = "Inertia",
            ["title"] = "Home"
        });

        var props = Props(document);
        Assert.Equal("Alice", props.GetProperty("auth").GetProperty("user").GetProperty("name").GetString());
        Assert.Equal("Inertia", props.GetProperty("auth").GetProperty("team").GetString());
        Assert.False(props.TryGetProperty("auth.team", out _));
        Assert.Equal("Home", props.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Dot_key_merges_into_shared_object_and_keeps_it_shared()
    {
        using var test = Inertia_();
        var inertia = test.GetRequiredService<InertiaContext>();
        var sharedAuth = new Dictionary<string, object?> { ["name"] = "Alice" };
        inertia.Share("auth", sharedAuth);

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", new Dictionary<string, object>
        {
            ["auth.can"] = new { edit = true }
        }));

        var auth = Props(document).GetProperty("auth");
        Assert.Equal("Alice", auth.GetProperty("name").GetString());
        Assert.True(auth.GetProperty("can").GetProperty("edit").GetBoolean());
        Assert.Equal(new[] { "auth" }, TestHelpers.Strings(document.RootElement.GetProperty("sharedProps")));
        Assert.Single(sharedAuth); // the caller's dictionary is not modified
    }

    [Fact]
    public async Task Dot_key_merges_into_an_async_lazy_shared_object()
    {
        using var test = Inertia_();
        var inertia = test.GetRequiredService<InertiaContext>();
        inertia.Share("auth", (Func<Task<object>>)(async () =>
        {
            await Task.Yield();
            return new { name = "Alice" };
        }));

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", new Dictionary<string, object>
        {
            ["auth.can"] = new { edit = true }
        }));

        var auth = Props(document).GetProperty("auth");
        Assert.Equal("Alice", auth.GetProperty("name").GetString());
        Assert.True(auth.GetProperty("can").GetProperty("edit").GetBoolean());
    }

    [Fact]
    public async Task Children_of_a_callback_value_bypass_partial_filters()
    {
        using var test = Inertia_(only: "auth.user");

        using var document = await Render(test, new Dictionary<string, object>
        {
            ["auth"] = (Func<object>)(() => new { user = "Alice", team = "Inertia" })
        });

        var auth = Props(document).GetProperty("auth");
        Assert.True(auth.TryGetProperty("user", out _));
        Assert.True(auth.TryGetProperty("team", out _));
    }

    [Fact]
    public async Task Nested_prop_types_inside_a_callback_value_are_resolved()
    {
        using var test = Inertia_();

        using var document = await Render(test, new Dictionary<string, object>
        {
            ["auth"] = (Func<object>)(() => new { user = "Alice", notifications = Inertia.Defer(() => 2) })
        });

        Assert.False(Props(document).GetProperty("auth").TryGetProperty("notifications", out _));
        Assert.Equal(new[] { "auth.notifications" }, TestHelpers.Strings(document.RootElement.GetProperty("deferredProps").GetProperty("default")));
    }

    [Fact]
    public async Task Nested_rescued_prop_reports_its_dot_path()
    {
        using var test = Inertia_(only: "auth.permissions");

        using var document = await Render(test, new
        {
            auth = new { permissions = Inertia.Defer(() => throw new InvalidOperationException("boom"), rescue: true) }
        });

        Assert.Equal(new[] { "auth.permissions" }, TestHelpers.Strings(document.RootElement.GetProperty("rescuedProps")));
        Assert.False(Props(document).GetProperty("auth").TryGetProperty("permissions", out _));
    }

    [Fact]
    public async Task Pocos_are_not_walked()
    {
        using var test = Inertia_(only: "report.title");

        using var document = await Render(test, new { report = new Poco("Report", "x") });

        // A POCO is opaque: a nested path selects it as a whole.
        Assert.Equal("Report", Props(document).GetProperty("report").GetProperty("title").GetString());
        Assert.Equal("x", Props(document).GetProperty("report").GetProperty("note").GetString());
    }

    static TestInfrastructure.TestContext Inertia_(string? only = null, string? except = null)
    {
        var test = TestInfrastructure.CreateContext();
        if (only != null || except != null)
            TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: only, except: except);
        else
            TestHelpers.AsInertia(test.HttpContext);

        return test;
    }

    static Task<JsonDocument> Render(TestInfrastructure.TestContext test, object props)
        => TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>().Render("Page", props));

    static JsonElement Props(JsonDocument document) => document.RootElement.GetProperty("props");

    public sealed record Poco(string Title, string? Note);
}
