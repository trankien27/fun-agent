using FunStudio.WindowsMaintenance.Agent.Constants;
using FunStudio.WindowsMaintenance.Agent.Executors;
using FunStudio.WindowsMaintenance.Agent.Models;
using FunStudio.WindowsMaintenance.Agent.Options;
using FunStudio.WindowsMaintenance.Agent.Services;
using Microsoft.Extensions.Options;

namespace FunStudio.WindowsMaintenance.Agent;

public static class LocalApiEndpoints
{
    public static WebApplication MapLocalApi(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            Status = "OK",
            UtcNow = DateTime.UtcNow
        }));

        app.MapGet("/api/local/status", (
            IConfiguration configuration,
            IOptions<AgentOptions> agentOptions,
            IOptions<LocalApiOptions> localApiOptions) =>
        {
            return Results.Ok(new
            {
                Status = "RUNNING",
                agentOptions.Value.MachineCode,
                agentOptions.Value.AgentVersion,
                LocalApi = new
                {
                    localApiOptions.Value.Enabled,
                    localApiOptions.Value.Urls,
                    localApiOptions.Value.RequireApiKey
                },
                ManagedServices = configuration.GetSection("ManagedServices").Get<string[]>() ?? [],
                ManagedFolders = configuration.GetSection(ManagedFolderOptions.SectionName).Get<Dictionary<string, string>>() ?? []
            });
        });

        app.MapGet("/api/local/task-types", () => Results.Ok(new[]
        {
            TaskTypes.GetServiceStatus,
            TaskTypes.StartService,
            TaskTypes.StopService,
            TaskTypes.RestartService,
            TaskTypes.GetFolderInfo,
            TaskTypes.ListFolderFiles,
            TaskTypes.DownloadToFolder,
            TaskTypes.ExtractZipToFolder,
            TaskTypes.DeleteFolderFile,
            TaskTypes.CleanFolder,
            TaskTypes.StartIis,
            TaskTypes.StopIis,
            TaskTypes.RestartIis,
            TaskTypes.RunPowerShellAdmin,
            TaskTypes.RunPowerShellUser,
            TaskTypes.RunPowerShellFileAdmin,
            TaskTypes.RunPowerShellFileUser,
            TaskTypes.GetUltraViewerPreferId,
            TaskTypes.GetTransactions,
            TaskTypes.PrintImage,
            TaskTypes.UpdateVersion,
            TaskTypes.DeployFsAsyncTransaction,
            TaskTypes.DeployFsUpdateSync,
            TaskTypes.DeployAppForm
        }));

        app.MapGet("/api/local/received-tasks", (
            ReceivedTaskLogStore store,
            HttpContext httpContext,
            IOptions<LocalApiOptions> localApiOptions) =>
        {
            if (!IsAuthorized(httpContext, localApiOptions.Value))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(store.GetRecent());
        });

        app.MapGet("/api/local/transactions", async (
            TransactionReader transactionReader,
            HttpContext httpContext,
            IOptions<LocalApiOptions> localApiOptions,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(httpContext, localApiOptions.Value))
            {
                return Results.Unauthorized();
            }

            try
            {
                var transactions = await transactionReader.GetTransactionsAsync(cancellationToken);
                return Results.Ok(transactions);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new
                {
                    Message = ex.Message
                });
            }
        });

        app.MapPost("/api/local/print-image", async (
            PrintImageRequest request,
            IHttpClientFactory httpClientFactory,
            HttpContext httpContext,
            IOptions<LocalApiOptions> localApiOptions,
            IOptions<PrintImageOptions> printImageOptions,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(httpContext, localApiOptions.Value))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.TransactionId))
            {
                return Results.BadRequest(new { Message = "transactionId is required." });
            }

            var client = httpClientFactory.CreateClient("print-image");
            using var response = await client.PostAsJsonAsync(printImageOptions.Value.Url, request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return Results.Json(new
            {
                StatusCode = (int)response.StatusCode,
                response.IsSuccessStatusCode,
                Body = body
            }, statusCode: (int)response.StatusCode);
        });

        app.MapPost("/api/local/tasks/run", async (
            AgentTaskMessage message,
            AgentTaskExecutor executor,
            HttpContext httpContext,
            IOptions<LocalApiOptions> localApiOptions,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(httpContext, localApiOptions.Value))
            {
                return Results.Unauthorized();
            }

            if (message.TaskId == Guid.Empty)
            {
                message.TaskId = Guid.NewGuid();
            }

            var logs = new List<object>();
            var result = await executor.ExecuteAsync(
                message,
                log =>
                {
                    logs.Add(new
                    {
                        Message = log,
                        CreatedAt = DateTime.UtcNow
                    });

                    return Task.CompletedTask;
                },
                cancellationToken);

            return Results.Ok(new
            {
                Logs = logs,
                Result = result
            });
        });

        return app;
    }

    private static bool IsAuthorized(HttpContext httpContext, LocalApiOptions options)
    {
        if (!options.RequireApiKey)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return false;
        }

        return httpContext.Request.Headers.TryGetValue("X-Local-Agent-Key", out var headerValue)
            && string.Equals(headerValue.ToString(), options.ApiKey, StringComparison.Ordinal);
    }
}
