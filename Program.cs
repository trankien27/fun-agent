using FunStudio.WindowsMaintenance.Agent;
using FunStudio.WindowsMaintenance.Agent.Options;
using FunStudio.WindowsMaintenance.Agent.Services;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

var runTaskIndex = Array.IndexOf(args, "--run-task");
var isLocalTaskRunner = runTaskIndex >= 0;

if (isLocalTaskRunner)
{
    var cliBuilder = Host.CreateApplicationBuilder(args);
    cliBuilder.Services.AddMaintenanceAgent(cliBuilder.Configuration);

    var cliHost = cliBuilder.Build();
    var taskFile = runTaskIndex + 1 < args.Length ? args[runTaskIndex + 1] : "";

    Environment.ExitCode = await LocalTaskRunner.RunAsync(cliHost, taskFile, CancellationToken.None);
    return;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService()
        ? AppContext.BaseDirectory
        : Directory.GetCurrentDirectory()
});

if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "FunStudioMaintenanceAgent";
    });
}

builder.Services.AddMaintenanceAgent(builder.Configuration);
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<TransactionErrorScanner>();

var localApiOptions = builder.Configuration
    .GetSection(LocalApiOptions.SectionName)
    .Get<LocalApiOptions>() ?? new LocalApiOptions();

foreach (var url in localApiOptions.Urls.Where(url => !string.IsNullOrWhiteSpace(url)))
{
    builder.WebHost.UseUrls(url);
}

var app = builder.Build();

if (app.Services.GetRequiredService<IOptions<LocalApiOptions>>().Value.Enabled)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapLocalApi();
}

await app.RunAsync();
