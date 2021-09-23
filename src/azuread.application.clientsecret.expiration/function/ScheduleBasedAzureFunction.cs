using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace azuread.application.clientsecret.expiration
{
    /// <summary>
    /// Represents the base functionality for an Azure Function implementation.
    /// </summary>
    public abstract class ScheduleBasedAzureFunction
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScheduleBasedAzureFunction"/> class.
        /// </summary>
        /// <param name="logger">The logger instance to write diagnostic messages throughout the execution of the HTTP trigger.</param>
        protected ScheduleBasedAzureFunction(ILogger logger)
        {
            Logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Gets the logger instance used throughout this Azure Function.
        /// </summary>
        protected ILogger Logger { get; }
    }
}