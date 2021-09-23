using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Arcus.Security.Core.Caching.Configuration;
using azuread.application.clientsecret.expiration;

[assembly: FunctionsStartup(typeof(Startup))]

namespace azuread.application.clientsecret.expiration
{
    public class Startup : FunctionsStartup
    {
        // This method gets called by the runtime. Use this method to configure the app configuration.
        public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
        {
            builder.ConfigurationBuilder.AddEnvironmentVariables();
        }
        
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
        public override void Configure(IFunctionsHostBuilder builder)
        {
            IConfiguration config = builder.GetContext().Configuration;           

            builder.ConfigureSecretStore(stores =>
            {
#if DEBUG
                stores.AddConfiguration(config);
#endif

                stores.AddEnvironmentVariables();

                var keyVaultUri = config.GetValue<string>("KEYVAULT_URI");
                stores.AddAzureKeyVaultWithManagedIdentity(keyVaultUri, cacheConfiguration: CacheConfiguration.Default);
            });

            var instrumentationKey = config.GetValue<string>("APPINSIGHTS_INSTRUMENTATIONKEY");
            var configuration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithComponentName("azuread.application.clientsecret.expiration")
                .Enrich.WithVersion()
#if !DEBUG
                .WriteTo.AzureApplicationInsights(instrumentationKey, LogEventLevel.Warning)
#endif
                .WriteTo.Console(LogEventLevel.Warning);

            builder.Services.AddLogging(logging =>
            {
                logging.ClearProvidersExceptFunctionProviders()
                       .AddSerilog(configuration.CreateLogger(), dispose: true);
            });
        }
    }
}
