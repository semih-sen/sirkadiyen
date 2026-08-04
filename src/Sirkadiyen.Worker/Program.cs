using Microsoft.AspNetCore.Builder;
using Sirkadiyen.Infrastructure.Configuration;
using Sirkadiyen.Worker.Composition;

DotEnvFile.Load();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.ConfigureWorkerHost();
builder.Services.AddWorkerApplication(builder.Configuration, builder.Environment);

WebApplication app = builder.Build();
app.MapWorkerHealthEndpoints();

await app.RunAsync();
