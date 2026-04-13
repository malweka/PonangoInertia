using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Xunit;

namespace Ponango.Inertia.Tests;

public class PrecognitionTests
{
    [Fact]
    public void Valid_precognition_short_circuits_before_action_execution()
    {
        using var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Headers["Precognition"] = "true";

        var filter = new PrecognitiveAttribute();
        var actionContext = TestInfrastructure.CreateActionContext(test.HttpContext);
        var executingContext = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            new object());

        filter.OnActionExecuting(executingContext);

        var result = Assert.IsType<StatusCodeResult>(executingContext.Result);
        Assert.Equal(204, result.StatusCode);
        Assert.Equal("true", test.HttpContext.Response.Headers["Precognition-Success"].ToString());
    }

    [Fact]
    public void Invalid_precognition_validate_only_returns_filtered_errors()
    {
        using var test = TestInfrastructure.CreateContext();
        test.HttpContext.Request.Headers["Precognition"] = "true";
        test.HttpContext.Request.Headers["Precognition-Validate-Only"] = "name";

        var filter = new PrecognitiveAttribute();
        var actionContext = TestInfrastructure.CreateActionContext(test.HttpContext);
        actionContext.ModelState.AddModelError("name", "Name is required");
        actionContext.ModelState.AddModelError("email", "Email is invalid");

        var executingContext = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            new object());

        filter.OnActionExecuting(executingContext);

        var result = Assert.IsType<ContentResult>(executingContext.Result);
        Assert.Equal(422, result.StatusCode);
        Assert.Contains("name", result.Content);
        Assert.DoesNotContain("email", result.Content);
    }
}
