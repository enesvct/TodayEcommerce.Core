using Hangfire;
using Microsoft.OpenApi.Models;
using TodayEcommerce.Core.Application;
using TodayEcommerce.Core.Infrastructure;
using TodayEcommerce.Core.WebApi.Authentication;
using TodayEcommerce.Core.WebApi.ErrorHandling;

var builder = WebApplication.CreateBuilder(args);

// Composition root: the only place that knows every layer and wires interfaces to implementations.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddBackgroundJobs(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Adds the "Authorize" button: paste the accessToken from POST /dev/token.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Development: call POST /dev/token, then paste its accessToken here.",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
    });
});

var app = builder.Build();

// First middleware: catches exceptions thrown anywhere further down the pipeline.
app.UseExceptionHandler();

// Empty error responses (e.g. 401/403 from authentication) also get a ProblemDetails body.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Job dashboard. Hangfire's default filter only allows requests from localhost;
    // a deployed environment needs a real authorization filter here.
    app.UseHangfireDashboard("/hangfire");

    // Demo tokens. Never mapped outside Development.
    app.MapDevTokenEndpoint();
}

app.UseHttpsRedirection();

app.UseAuthentication(); // who are you? (reads and validates the token)
app.UseAuthorization();  // are you allowed? (policies, roles)

app.MapControllers();

app.Run();

/// <summary>Exposed so integration tests can host the API in memory (WebApplicationFactory).</summary>
public partial class Program;
