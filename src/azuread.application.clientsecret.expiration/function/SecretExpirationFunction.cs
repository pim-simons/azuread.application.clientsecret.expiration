using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Threading.Tasks;
using Arcus.EventGrid.Publishing;
using Arcus.Security.Core;
using CloudNative.CloudEvents;
using GuardNet;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Newtonsoft.Json.Linq;

namespace azuread.application.clientsecret.expiration
{
    /// <summary>
    /// Represents the root endpoint of the Azure Function.
    /// </summary>
    public class SecretExpirationFunction : ScheduleBasedAzureFunction
    {
        private readonly ISecretProvider _secretProvider;
        private HttpClient httpClient = new HttpClient();

        /// <summary>
        /// Initializes a new instance of the <see cref="SecretExpirationFunction"/> class.
        /// </summary>
        /// <param name="secretProvider">The instance that provides secrets to the HTTP trigger.</param>
        /// <param name="logger">The logger instance to write diagnostic trace messages while handling the HTTP request.</param>
        /// <exception cref="ArgumentNullException">Thrown when the <paramref name="secretProvider"/> is <c>null</c>.</exception>
        public SecretExpirationFunction(ISecretProvider secretProvider, ILogger<SecretExpirationFunction> logger) : base(logger)
        {
            Guard.NotNull(secretProvider, nameof(secretProvider), "Requires a secret provider instance");
            _secretProvider = secretProvider;
        }

        [FunctionName("SecretExpirationFunction")]
        public async Task Run([TimerTrigger("0 0 0 * * *", RunOnStartup = false)] TimerInfo timer)
        {
            try
            {
                AuthenticationResult authenticationResult = await GetToken();
                string applications = await GetApplications(authenticationResult);
                List<CloudEvent> events = new List<CloudEvent>();

                foreach (var application in JArray.Parse(JObject.Parse(applications)["value"].ToString()))
                {
                    JArray passwordCredentials = JArray.Parse(application["passwordCredentials"].ToString());

                    if (passwordCredentials != null && passwordCredentials.Count > 0)
                    {
                        foreach(JToken passwordCredential in passwordCredentials)
                        {
                            DateTime endDateTime = DateTime.Parse(passwordCredential["endDateTime"].ToString());
                            double remainingValidDays = (endDateTime - DateTime.UtcNow).TotalDays;
                            string applicationName = application["displayName"].ToString();
                            string keyId = passwordCredential["keyId"].ToString();

                            var telemetryContext = new Dictionary<string, object>();
                            telemetryContext.Add("keyId", keyId);
                            telemetryContext.Add("applicationName", applicationName);
                            telemetryContext.Add("remainingValidDays", remainingValidDays);

                            if (remainingValidDays <= 14 && remainingValidDays > 0)
                            {
                                Logger.LogEvent(String.Format("{0} has a client secret that will expire in {1} days", applicationName, remainingValidDays), telemetryContext);

                                events.Add(CreateEvent(applicationName, keyId, endDateTime, "ClientSecretAboutToExpire"));
                            }
                            else if (remainingValidDays <= 0)
                            {
                                Logger.LogEvent(String.Format("{0} has a client secret that has expired", applicationName), telemetryContext);

                                events.Add(CreateEvent(applicationName, keyId, endDateTime, "ClientSecretExpired"));
                            }
                        }
                    }
                }

                if (events.Count > 0)
                {
                    await PublishEvents(events);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, ex.Message);
            }
        }

        private async Task<AuthenticationResult> GetToken()
        {
            string clientId = await _secretProvider.GetRawSecretAsync("clientId");
            string clientSecret = await _secretProvider.GetRawSecretAsync("clientSecret");
            string tenantId = await _secretProvider.GetRawSecretAsync("tenantId");

            var ccab = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithTenantId(tenantId)
                .Build();

            AcquireTokenForClientParameterBuilder tokenResult = ccab.AcquireTokenForClient(new List<string> { "https://graph.microsoft.com/.default" });
            AuthenticationResult token = await tokenResult.ExecuteAsync();

            return token;
        }

        private async Task<string> GetApplications(AuthenticationResult authenticationResult)
        {
            var durationMeasurement = new Stopwatch();
            durationMeasurement.Start();
            var startTime = DateTimeOffset.UtcNow;
            HttpRequestMessage request = null;
            HttpStatusCode responseStatusCode = HttpStatusCode.OK;

            try
            {
                request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/applications");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authenticationResult.AccessToken);

                HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(request);
                responseStatusCode = httpResponseMessage.StatusCode;

                if (!httpResponseMessage.IsSuccessStatusCode)
                {
                    throw new Exception(httpResponseMessage.ReasonPhrase);
                }

                return await httpResponseMessage.Content.ReadAsStringAsync();
            }
            finally
            {
                Logger.LogHttpDependency(request, responseStatusCode, startTime: startTime, duration: durationMeasurement.Elapsed);
            }
        }

        private CloudEvent CreateEvent(string applicationName, string keyId, DateTime endDateTime, string type)
        {
            string eventSubject = $"/appregistrations/clientsecrets/{keyId}";
            string eventId = Guid.NewGuid().ToString();

            CloudEvent @event = new CloudEvent(
                                    CloudEventsSpecVersion.V1_0,
                                    type,
                                    new Uri("https://github.com/pim-simons/azuread.application.clientsecret.expiration/"),
                                    eventSubject,
                                    eventId)
            {
                Data = new models.ClientSecret()
                {
                    displayName = applicationName,
                    endDateTime = endDateTime,
                    keyId = keyId
                },
                DataContentType = new ContentType("application/json")
            };

            return @event;
        }

        private async Task PublishEvents(List<CloudEvent> events)
        {
            string topicEndpoint = await _secretProvider.GetRawSecretAsync("topicEndpoint");
            string endpointKey = await _secretProvider.GetRawSecretAsync("endpointKey");

            var eventGridPublisher = EventGridPublisherBuilder
                .ForTopic(topicEndpoint)
                .UsingAuthenticationKey(endpointKey)
                .Build();

            foreach (CloudEvent @event in events)
            {
                await eventGridPublisher.PublishAsync(@event);
            }
        }
    }
}