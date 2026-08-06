using Sirkadiyen.Api.Composition;
using Sirkadiyen.Infrastructure.Configuration;

// Before the builder, because the environment-variable provider reads the process environment as
// it is added. A deployed host injects its own variables and ships no file, so this does nothing
// there (ADR-041).
DotEnvFile.Load();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddSirkadiyenApi();

WebApplication app = builder.Build();
app.UseSirkadiyenApiPipeline();
app.MapSirkadiyenApiEndpoints();

app.Run();

public partial class Program;
