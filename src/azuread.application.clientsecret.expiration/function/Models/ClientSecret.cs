using System;

namespace azuread.application.clientsecret.expiration.models
{
    public class ClientSecret
    {
        public string displayName { get; set; }
        public DateTime endDateTime { get; set; }
        public string keyId { get; set; }
    }
}