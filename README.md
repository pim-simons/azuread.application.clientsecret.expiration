# Azure AD Application ClientSecret Expiration Checker
### Functionality
Applications within Azure Active Directory can have multiple client secrets, unfortunately there is no out-of-the-box way of getting notified when a secret is about to expire. This scheduled Azure Function will get a list of all applications and check if there are any client secrets that are about the expire (in less than 2 weeks) or have already expired and publish a CloudEvent to an Event Grid Topic.

### Configuration
The following configuration items need to be set:
- topicEndpoint (the endpoint of the Event Grid Topic you want to publish the events to)
- endpointKey (the key of the Event Grid Topic you want to publish the events to)

The Function uses Managed Identity to authenticate to Azure Active Directory, this can be configured according to: https://docs.microsoft.com/en-us/azure/app-service/scenario-secure-app-access-microsoft-graph-as-app?tabs=azure-cli%2Ccommand-line#grant-access-to-microsoft-graph. Note that the function you are using to call the Microsoft Graph API will need Application.Read.All permission. 