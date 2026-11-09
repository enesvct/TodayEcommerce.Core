using Hangfire;
using TodayEcommerce.Core.Application;
using TodayEcommerce.Core.Infrastructure;
using TodayEcommerce.Core.WebApi.ErrorHandling;

var builder = WebApplication.CreateBuilder(args);

// Composition root: the only place that knows every layer and wires interfaces to implementations.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddBackgroundJobs(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// First middleware: catches exceptions thrown anywhere further down the pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Job dashboard. Hangfire's default filter only allows requests from localhost;
    // a deployed environment needs a real authorization filter here.
    app.UseHangfireDashboard("/hangfire");
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Exposed so integration tests can host the API in memory (WebApplicationFactory).</summary>
public partial class Program;
