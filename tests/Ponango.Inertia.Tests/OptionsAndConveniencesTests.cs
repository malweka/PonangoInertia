using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// Smaller v3 options: all errors per field, shared-key exposure, lazy delegate props.
/// </summary>
public class OptionsAndConveniencesTests
{
    // ----- All errors per field -----

    [Fact]
    public async Task Errors_default_to_first_message_per_field()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>()
            .Render("Users/Create", new { })
            .WithErrors(InvalidState()));

        Assert.Equal("Email is required", Errors(document).GetProperty("email").GetString());
    }

    [Fact]
    public async Task WithAllErrors_option_emits_every_message_per_field()
    {
        using var test = TestInfrastructure.CreateContext(options => options.WithAllErrors = true);
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>()
            .Render("Users/Create", new { })
            .WithErrors(InvalidState()));

        var email = Errors(document).GetProperty("email");
        Assert.Equal(JsonValueKind.Array, email.ValueKind);
        Assert.Equal(new[] { "Email is required", "Email is invalid" }, email.EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { "Name is required" }, Errors(document).GetProperty("name").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task WithAllErrors_option_applies_inside_error_bags()
    {
        using var test = TestInfrastructure.CreateContext(options => options.WithAllErrors = true);
        TestHelpers.AsInertia(test.HttpContext);
        test.HttpContext.Request.Headers["X-Inertia-Error-Bag"] = "createUser";

        using var document = await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>()
            .Render("Users/Create", new { })
            .WithErrors(InvalidState()));

        var email = Errors(document).GetProperty("createUser").GetProperty("email");
        Assert.Equal(2, email.GetArrayLength());
    }

    // ----- Shared prop key exposure -----

    [Fact]
    public async Task Shared_prop_keys_are_exposed_by_default()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var inertia = test.GetRequiredService<InertiaContext>();
        inertia.Share("auth", new { name = "Alice" });

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", new { }));

        Assert.Equal(new[] { "auth" }, TestHelpers.Strings(document.RootElement.GetProperty("sharedProps")));
    }

    [Fact]
    public async Task Shared_prop_keys_can_be_hidden_while_values_are_still_sent()
    {
        using var test = TestInfrastructure.CreateContext(options => options.ExposeSharedPropKeys = false);
        TestHelpers.AsInertia(test.HttpContext);
        var inertia = test.GetRequiredService<InertiaContext>();
        inertia.Share("auth", new { name = "Alice" });

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", new { }));

        Assert.False(document.RootElement.TryGetProperty("sharedProps", out _));
        Assert.Equal("Alice", document.RootElement.GetProperty("props").GetProperty("auth").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Shared_prop_keys_stay_listed_when_a_partial_reload_skips_their_values()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Page", only: "stats");
        var inertia = test.GetRequiredService<InertiaContext>();
        inertia.Share("auth", new { name = "Alice" });

        using var document = await TestHelpers.ExecuteJsonAsync(test, inertia.Render("Page", new { stats = Inertia.Defer(() => 5) }));

        Assert.False(document.RootElement.GetProperty("props").TryGetProperty("auth", out _));
        Assert.Equal(new[] { "auth" }, TestHelpers.Strings(document.RootElement.GetProperty("sharedProps")));
    }

    // ----- Serializer options -----

    [Fact]
    public void Serializer_options_are_built_once_per_builder()
    {
        var configured = 0;
        var serializer = new TestHelpers.SerializerOptionsBuilder(_ => configured++);
        PageModel Page(bool bigIntegers) => new()
        {
            Component = "Page",
            Url = "/",
            Version = "v",
            Props = new { id = long.MaxValue },
            PreserveBigIntegers = bigIntegers ? true : null
        };

        var plain = Page(bigIntegers: false).ToJson(serializer);
        var marked = Page(bigIntegers: true).ToJson(serializer);
        Page(bigIntegers: false).ToJson(serializer);
        Page(bigIntegers: true).ToJson(serializer);

        Assert.Equal(1, configured);
        Assert.DoesNotContain("$bigint", plain);
        Assert.Contains("$bigint", marked);
    }

    // ----- Lazy delegate props -----

    [Fact]
    public async Task Lazy_delegate_prop_is_not_called_when_a_partial_reload_excludes_it()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Users/Index", only: "users");
        var calls = 0;

        using var document = await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>()
            .Render("Users/Index", new Dictionary<string, object>
            {
                ["users"] = new[] { "alice" },
                ["companies"] = (Func<object>)(() => { calls++; return new[] { "acme" }; })
            }));

        Assert.Equal(0, calls);
        Assert.False(document.RootElement.GetProperty("props").TryGetProperty("companies", out _));
    }

    [Fact]
    public async Task Lazy_delegate_prop_is_called_once_on_a_full_visit()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        var calls = 0;

        using var document = await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>()
            .Render("Users/Index", new Dictionary<string, object>
            {
                ["companies"] = (Func<object>)(() => { calls++; return new[] { "acme" }; })
            }));

        Assert.Equal(1, calls);
        Assert.Equal("acme", document.RootElement.GetProperty("props").GetProperty("companies")[0].GetString());
    }

    [Fact]
    public async Task Async_lazy_delegate_props_are_awaited()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>()
            .Render("Users/Index", new Dictionary<string, object>
            {
                ["count"] = (Func<Task<object>>)(async () => { await Task.Yield(); return 3; }),
                ["names"] = (Func<Task<string[]>>)(async () => { await Task.Yield(); return new[] { "a" }; }),
                ["typed"] = (Func<string>)(() => "covariant")
            }));

        var props = document.RootElement.GetProperty("props");
        Assert.Equal(3, props.GetProperty("count").GetInt32());
        Assert.Equal("a", props.GetProperty("names")[0].GetString());
        Assert.Equal("covariant", props.GetProperty("typed").GetString());
    }

    [Fact]
    public async Task Lazy_delegates_returning_value_types_are_evaluated()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);

        // A lambda returning an int is a Func<int>, which is not a Func<object>.
        using var document = await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>()
            .Render("Users/Index", new Dictionary<string, object>
            {
                ["count"] = () => 42,
                ["asyncCount"] = () => Task.FromResult(7),
                ["valueTaskCount"] = () => new ValueTask<int>(9)
            })
            .With("enabled", () => true));

        var props = document.RootElement.GetProperty("props");
        Assert.Equal(42, props.GetProperty("count").GetInt32());
        Assert.Equal(7, props.GetProperty("asyncCount").GetInt32());
        Assert.Equal(9, props.GetProperty("valueTaskCount").GetInt32());
        Assert.True(props.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Value_type_lazy_delegate_is_not_called_when_a_partial_reload_excludes_it()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext, partialComponent: "Users/Index", only: "users");
        var calls = 0;

        using var document = await TestHelpers.ExecuteJsonAsync(test, test.GetRequiredService<InertiaContext>()
            .Render("Users/Index", new Dictionary<string, object>
            {
                ["users"] = new[] { "alice" },
                ["count"] = () => ++calls
            }));

        Assert.Equal(0, calls);
        Assert.False(document.RootElement.GetProperty("props").TryGetProperty("count", out _));
    }

    [Fact]
    public async Task Lazy_delegate_exceptions_surface_unwrapped()
    {
        using var test = TestInfrastructure.CreateContext();
        TestHelpers.AsInertia(test.HttpContext);
        Func<int> failing = () => throw new InvalidOperationException("boom");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestHelpers.ExecuteJsonAsync(
            test,
            test.GetRequiredService<InertiaContext>().Render("Users/Index", new Dictionary<string, object> { ["count"] = failing })));

        Assert.Equal("boom", exception.Message);
    }

    static ModelStateDictionary InvalidState()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("email", "Email is required");
        state.AddModelError("email", "Email is invalid");
        state.AddModelError("name", "Name is required");
        return state;
    }

    static JsonElement Errors(JsonDocument document) => document.RootElement.GetProperty("props").GetProperty("errors");
}
