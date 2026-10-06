using System.Numerics;
using System.Text.Json;
using Xunit;

namespace Ponango.Inertia.Tests;

/// <summary>
/// Big integer transport (https://inertiajs.com/docs/v3/advanced/big-integers).
/// </summary>
public class BigIntegerTests
{
    const long Big = 900719925474099988L;
    const long MaxSafe = 9007199254740991L;

    [Fact]
    public async Task Disabled_by_default_emits_plain_numbers_and_no_flag()
    {
        using var test = CreateInertiaTest();

        using var document = await Render(test, new { id = Big });

        Assert.Equal(JsonValueKind.Number, Props(document).GetProperty("id").ValueKind);
        Assert.False(document.RootElement.TryGetProperty("preserveBigIntegers", out _));
    }

    [Fact]
    public async Task Global_option_wraps_unsafe_long_and_sets_flag()
    {
        using var test = CreateInertiaTest(enabled: true);

        using var document = await Render(test, new { id = Big });

        AssertMarker(Props(document).GetProperty("id"), "900719925474099988");
        Assert.True(document.RootElement.GetProperty("preserveBigIntegers").GetBoolean());
    }

    [Fact]
    public async Task Safe_range_values_stay_numbers()
    {
        using var test = CreateInertiaTest(enabled: true);

        using var document = await Render(test, new { max = MaxSafe, min = -MaxSafe, small = 42L, plain = 7 });

        var props = Props(document);
        Assert.Equal(MaxSafe, props.GetProperty("max").GetInt64());
        Assert.Equal(-MaxSafe, props.GetProperty("min").GetInt64());
        Assert.Equal(42, props.GetProperty("small").GetInt64());
        Assert.Equal(7, props.GetProperty("plain").GetInt32());
    }

    [Fact]
    public async Task Negative_unsafe_long_is_wrapped()
    {
        using var test = CreateInertiaTest(enabled: true);

        using var document = await Render(test, new { id = -MaxSafe - 1 });

        AssertMarker(Props(document).GetProperty("id"), "-9007199254740992");
    }

    [Fact]
    public async Task Ulong_Int128_and_BigInteger_are_wrapped()
    {
        using var test = CreateInertiaTest(enabled: true);

        using var document = await Render(test, new
        {
            u = ulong.MaxValue,
            i128 = Int128.MaxValue,
            u128 = (UInt128)MaxSafe + 1,
            big = BigInteger.Parse("123456789012345678901234567890"),
            smallBig = new BigInteger(5)
        });

        var props = Props(document);
        AssertMarker(props.GetProperty("u"), "18446744073709551615");
        AssertMarker(props.GetProperty("i128"), Int128.MaxValue.ToString());
        AssertMarker(props.GetProperty("u128"), "9007199254740992");
        AssertMarker(props.GetProperty("big"), "123456789012345678901234567890");
        Assert.Equal(5, props.GetProperty("smallBig").GetInt32());
    }

    [Fact]
    public async Task Nested_values_in_anonymous_objects_arrays_and_dictionaries_are_wrapped()
    {
        using var test = CreateInertiaTest(enabled: true);

        using var document = await Render(test, new
        {
            order = new { id = Big, lines = new[] { new { sku = Big + 1 } } },
            lookup = new Dictionary<string, object> { ["a"] = Big },
            boxed = (object)Big,
            keyed = new Dictionary<long, string> { [Big] = "x" }
        });

        var props = Props(document);
        AssertMarker(props.GetProperty("order").GetProperty("id"), "900719925474099988");
        AssertMarker(props.GetProperty("order").GetProperty("lines")[0].GetProperty("sku"), "900719925474099989");
        AssertMarker(props.GetProperty("lookup").GetProperty("a"), "900719925474099988");
        AssertMarker(props.GetProperty("boxed"), "900719925474099988");
        Assert.Equal("x", props.GetProperty("keyed").GetProperty("900719925474099988").GetString());
    }

    [Fact]
    public async Task Nullable_long_is_wrapped_and_null_stays_null()
    {
        using var test = CreateInertiaTest(enabled: true);

        using var document = await Render(test, new { set = new Holder(Big), unset = new Holder(null) });

        var props = Props(document);
        AssertMarker(props.GetProperty("set").GetProperty("value"), "900719925474099988");
        Assert.False(props.GetProperty("unset").TryGetProperty("value", out _)); // WhenWritingNull default
    }

    [Fact]
    public async Task Per_response_opt_in_and_opt_out_override_global()
    {
        using (var test = CreateInertiaTest())
        {
            using var optIn = await Render(test, new { id = Big }, result => result.WithPreserveBigIntegers());
            AssertMarker(Props(optIn).GetProperty("id"), "900719925474099988");
        }

        using (var test = CreateInertiaTest(enabled: true))
        {
            using var optOut = await Render(test, new { id = Big }, result => result.WithPreserveBigIntegers(false));
            Assert.Equal(Big, Props(optOut).GetProperty("id").GetInt64());
            Assert.False(optOut.RootElement.TryGetProperty("preserveBigIntegers", out _));
        }
    }

    [Fact]
    public async Task Flash_big_integer_is_wrapped()
    {
        using var test = CreateInertiaTest(enabled: true);
        test.GetRequiredService<InertiaContext>().Flash("newOrder", new { id = Big, count = 3 });

        using var document = await Render(test, new { });

        var flash = document.RootElement.GetProperty("flash").GetProperty("newOrder");
        AssertMarker(flash.GetProperty("id"), "900719925474099988");
        Assert.Equal(3, flash.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task JsonElement_props_keep_fractions_and_wrap_unsafe_integers()
    {
        using var test = CreateInertiaTest(enabled: true);
        using var source = JsonDocument.Parse("""{"id":900719925474099988,"ratio":1.5,"exp":1e300,"name":"x","list":[1,-900719925474099988]}""");

        using var document = await Render(test, new { data = source.RootElement.Clone() });

        var data = Props(document).GetProperty("data");
        AssertMarker(data.GetProperty("id"), "900719925474099988");
        Assert.Equal(1.5, data.GetProperty("ratio").GetDouble());
        Assert.Equal(1e300, data.GetProperty("exp").GetDouble());
        Assert.Equal("x", data.GetProperty("name").GetString());
        Assert.Equal(1, data.GetProperty("list")[0].GetInt32());
        AssertMarker(data.GetProperty("list")[1], "-900719925474099988");
    }

    [Fact]
    public void Html_initial_payload_wraps_big_integers_and_sets_flag()
    {
        var html = TestHelpers.RenderPage(new PageModel
        {
            Component = "Orders/Show",
            Url = "/orders/1",
            Version = "v",
            Props = new { id = Big },
            PreserveBigIntegers = true
        }, new TestHelpers.SerializerOptionsBuilder());

        using var page = JsonDocument.Parse(TestHelpers.ExtractScriptBody(html));
        AssertMarker(page.RootElement.GetProperty("props").GetProperty("id"), "900719925474099988");
        Assert.True(page.RootElement.GetProperty("preserveBigIntegers").GetBoolean());
    }

    [Fact]
    public async Task Markers_are_not_emitted_for_decimal_or_double()
    {
        using var test = CreateInertiaTest(enabled: true);

        using var document = await Render(test, new { d = 900719925474099988m, f = 9.007199254740993e15 });

        Assert.Equal(JsonValueKind.Number, Props(document).GetProperty("d").ValueKind);
        Assert.Equal(JsonValueKind.Number, Props(document).GetProperty("f").ValueKind);
    }

    [Fact]
    public async Task Big_integer_converter_takes_precedence_over_user_converters()
    {
        using var test = TestInfrastructure.CreateContext(options =>
        {
            options.PreserveBigIntegers = true;
            options.JsonSerializerOptions = serializer =>
            {
                serializer.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                serializer.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.WriteAsString;
            };
        });
        TestHelpers.AsInertia(test.HttpContext);

        using var document = await Render(test, new { id = Big });

        AssertMarker(Props(document).GetProperty("id"), "900719925474099988");
    }

    static TestInfrastructure.TestContext CreateInertiaTest(bool enabled = false)
    {
        var test = TestInfrastructure.CreateContext(options => options.PreserveBigIntegers = enabled);
        TestHelpers.AsInertia(test.HttpContext);
        return test;
    }

    static Task<JsonDocument> Render(TestInfrastructure.TestContext test, object props, Action<InertiaResult>? configure = null)
    {
        var result = test.GetRequiredService<InertiaContext>().Render("Page", props);
        configure?.Invoke(result);
        return TestHelpers.ExecuteJsonAsync(test, result);
    }

    static JsonElement Props(JsonDocument document) => document.RootElement.GetProperty("props");

    static void AssertMarker(JsonElement element, string digits)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal(digits, element.GetProperty("$bigint").GetString());
        Assert.Single(element.EnumerateObject());
    }

    private sealed record Holder(long? Value);
}
