using FunStudio.WindowsMaintenance.Agent.Executors;
using FunStudio.WindowsMaintenance.Agent.Options;
using FunStudio.WindowsMaintenance.Agent.Services;

namespace FunStudio.WindowsMaintenance.Agent;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMaintenanceAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
        services.Configure<LocalApiOptions>(configuration.GetSection(LocalApiOptions.SectionName));
        services.Configure<PowerShellOptions>(configuration.GetSection(PowerShellOptions.SectionName));
        services.Configure<ManagedFolderOptions>(configuration.GetSection(ManagedFolderOptions.SectionName));
        services.Configure<TransactionOptions>(configuration.GetSection(TransactionOptions.SectionName));
        services.Configure<TransactionErrorScannerOptions>(configuration.GetSection(TransactionErrorScannerOptions.SectionName));
        services.Configure<PrintImageOptions>(configuration.GetSection(PrintImageOptions.SectionName));

        services.AddHttpClient("agent-download");
        services.AddHttpClient("central-api");
        services.AddHttpClient("print-image");

        services.AddSingleton<PowerShellExecutor>();
        services.AddSingleton<IisExecutor>();
        services.AddSingleton<WindowsServiceExecutor>();
        services.AddSingleton<ManagedFolderExecutor>();
        services.AddSingleton<AgentTaskExecutor>();
        services.AddSingleton<ReceivedTaskLogStore>();
        services.AddSingleton<TransactionReader>();

        return services;
    }
}
