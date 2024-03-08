using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Text;
using Grpc.Core;
using Microsoft.AspNetCore.WebUtilities;
    

puclic class GeoDocClient
{
    private readonly HttpClient _client = new HttpClient();
    private string _brearerToken = "";

    public async Task AuthenticateAsync(string clientID, string clientSecret, string scope)
    {
        var tokenEndpoint = "https://login.microsoftonline.com/braarkivb2cprod.onmicrosoft.com/oauth2/v2.0/token";
        var requestBody = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", client_id),
            new KeyValuePair<string, string>("scope", scope),
            new KeyValuePair<string, string>("client_secret", client_secret),
            new KeyValuePair<string, string>("grant_type", "client_credentials")

        });

        var response = await _client.PostAsync(tokenEndpoint, requestBody);
        response.EnsureSuccessStatusCode();
        var jasonContent = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonConvert.DeserializeObject<Dictionary<string, string>>(jasonContent);

        if (tokenResponse != null && tokenResponse.ContainsKey("access_token"))
        {
            _brearerToken = tokenResponse["access_token"];
        }
        else
        {
            throw new Exception ("failed to authenticate.");
        }
    }
    
    public async Task<string> SearchDocumentsAsync()
    {
        if (string.IsNullOrWhiteSpace(_brearerToken))
        {
            throw new Invalid
        }
    }



}

    
    
    public async Task<String> GetGeoDocRecords(string gnr, string bnr, string snr)
    {
        HttpClient geoDoc = new()
        {
            BaseAddress = new Uri("https://api.geodoc.no")
        };

        using HttpResponseMessage response = await geoDoc.GetAsync("/v1/tenants/{tenant}/records");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }