using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;

namespace TodayEcommerce.Core.ArchitectureTests;

/// <summary>
/// Guards the Clean Architecture dependency rule: dependencies may only point inward.
/// The compiler only prevents circular project references; these tests catch the rest
/// (e.g. a framework package leaking into Domain, or a controller reaching into Infrastructure).
/// </summary>
public class LayerDependencyTests
{
    private const string DomainNamespace = "TodayEcommerce.Core.Domain";
    private const string ApplicationNamespace = "TodayEcommerce.Core.Application";
    private const string InfrastructureNamespace = "TodayEcommerce.Core.Infrastructure";
    private const string WebApiNamespace = "TodayEcommerce.Core.WebApi";

    private const string EntityFrameworkCore = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Redis = "StackExchange.Redis";
    private const string DistributedCache = "Microsoft.Extensions.Caching";

    private static readonly Assembly DomainAssembly = typeof(Domain.AssemblyReference).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.AssemblyReference).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.AssemblyReference).Assembly;
    private static readonly Assembly WebApiAssembly = typeof(WebApi.AssemblyReference).Assembly;

    [Fact]
    public void Domain_Should_Not_DependOn_AnyOtherLayer_Or_Framework()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ApplicationNamespace,
                InfrastructureNamespace,
                WebApiNamespace,
                EntityFrameworkCore,
                AspNetCore,
                Redis,
                "MediatR",
                "FluentValidation")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Application_Should_Not_DependOn_Infrastructure_WebApi_Or_Technologies()
    {
        // Application talks to ICacheService only; which cache technology sits behind it is Infrastructure's business.
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                InfrastructureNamespace,
                WebApiNamespace,
                EntityFrameworkCore,
                AspNetCore,
                Redis,
                DistributedCache)
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Infrastructure_Should_Not_DependOn_WebApi()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(WebApiNamespace)
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Controllers_Should_Not_DependOn_Infrastructure_Or_EfCore()
    {
        // WebApi references Infrastructure only to compose DI in Program.cs.
        // Controllers must talk to Application (MediatR), never to persistence directly.
        // Controllers are identified by inheritance, not by name: a class named "OrdersEndpoint"
        // that derives from ControllerBase is still a controller and must obey the rule.
        var result = Types.InAssembly(WebApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, EntityFrameworkCore)
            .GetResult();

        AssertSuccessful(result);
    }

    private static void AssertSuccessful(TestResult result)
    {
        var offenders = result.FailingTypeNames ?? Array.Empty<string>();
        Assert.True(
            result.IsSuccessful,
            $"Architecture rule violated by: {string.Join(", ", offenders)}");
    }
}
