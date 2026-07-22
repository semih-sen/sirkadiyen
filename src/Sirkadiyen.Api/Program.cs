using System.Text.Json.Serialization;
using Sirkadiyen.Api.Administration;
using Sirkadiyen.Application.ScheduleDiffing;
using Sirkadiyen.Application.SchedulePublication;
using Sirkadiyen.Infrastructure.Configuration;
using Sirkadiyen.Infrastructure.Persistence;

// Before the builder, because the environment-variable provider reads the
// process environment as it is added. A deployed host injects its own variables
// and ships no file, so this does nothing there (ADR-041).
DotEnvFile.Load();

var builder = WebApplication.CreateBuilder(args);

string connectionString = Required(
    builder.Configuration,
    "SIRKADIYEN_DATABASE:CONNECTION_STRING");

// Required rather than optional. The administrative endpoints can push a
// quarantined schedule into student calendars, so an unset key must stop the
// process from starting instead of silently leaving them open.
string adminApiKey = Required(builder.Configuration, "SIRKADIYEN_ADMIN:API_KEY");

builder.Services.AddProblemDetails();

// States and rules are read by a person deciding whether to approve a revision.
// "ReviewRequired" tells them something; "2" makes them go and count the enum.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new AdminApiOptions { ApiKey = adminApiKey });
builder.Services.AddScoped<AdminApiKeyFilter>();
builder.Services.AddScoped<ScheduleRevisionPublicationService>();
builder.Services.AddScoped<ScheduleDiffReviewService>();
builder.Services.AddSirkadiyenPersistence(connectionString);

var app = builder.Build();

app.UseExceptionHandler();
app.MapHealthChecks("/health");
app.MapOpenApi();
app.MapRevisionEndpoints();
app.MapDiffEndpoints();

app.Run();

static string Required(IConfiguration configuration, string key) =>
    configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException(
            $"Required configuration '{key}' is missing. Set it in the repository's '.env' "
            + $"file as '{key.Replace(":", "__", StringComparison.Ordinal)}' or export it as "
            + "an environment variable.");

public partial class Program;
