using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Veyra.Access.Api;

namespace Veyra.Access.Tests.Api;

public class RequireHttpsForSessionAttributeTests
{
    [Fact]
    public void OnResourceExecuting_PlainHttpInProduction_AnswersTheHttpsProblem()
    {
        var context = ContextFor(Environments.Production, https: false);

        new RequireHttpsForSessionAttribute().OnResourceExecuting(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal(RequireHttpsForSessionAttribute.Title, Assert.IsType<ProblemDetails>(result.Value).Title);
    }

    [Fact]
    public void OnResourceExecuting_HttpsInProduction_LetsTheRequestThrough()
    {
        var context = ContextFor(Environments.Production, https: true);

        new RequireHttpsForSessionAttribute().OnResourceExecuting(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void OnResourceExecuting_PlainHttpInDevelopment_LetsTheRequestThrough()
    {
        // Development cookies follow the request's scheme, so plain HTTP works there (local runs, the Vite proxy).
        var context = ContextFor(Environments.Development, https: false);

        new RequireHttpsForSessionAttribute().OnResourceExecuting(context);

        Assert.Null(context.Result);
    }

    private static ResourceExecutingContext ContextFor(string environment, bool https)
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new FakeEnvironment(environment))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Scheme = https ? "https" : "http";

        return new ResourceExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new List<IValueProviderFactory>());
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Veyra.Api";

        public string ContentRootPath { get; set; } = "";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
