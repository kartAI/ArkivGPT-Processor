using Azure.Core;
using Newtonsoft.Json;
using JsonConverter = System.Text.Json.Serialization.JsonConverter;

namespace ArkivGPT_Processor.Services;

public class GeoDocClient
{
    private readonly HttpClient _client = new HttpClient();
    private string _bearerToken = "";
    private string clientId = "c7b44674-09b2-4926-8957-8f3ec1ce4daf";
    private string clientSecret = "ePS8Q~3gaHitYCKItbK-UzCSKVSS51RgrCkNedBD";
    private string scope = "https://braarkivb2cprod.onmicrosoft.com/app-api-prod/.default";

    public async Task AuthenticateAsync()
    {
        var tokenEndpoint = "https://login.microsoftonline.com/braarkivb2cprod.onmicrosoft.com/oauth2/v2.0/token";
        var requestBody = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("scope", scope),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("grant_type", "client_credentials")

        });

        var response = await _client.PostAsync(tokenEndpoint, requestBody);
        response.EnsureSuccessStatusCode();
        var jasonContent = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonConvert.DeserializeObject<Dictionary<string, string>>(jasonContent);

        if (tokenResponse != null && tokenResponse.ContainsKey("access_token"))
        {
            _bearerToken = tokenResponse["access_token"];
        }
        else
        {
            throw new Exception("failed to authenticate.");
        }
    }

    public async Task<List<dynamic>> SearchDocumentsAsyncVedtak(int gnr, int bnr)
    {
        if (string.IsNullOrWhiteSpace(_bearerToken))
        {
            Console.WriteLine("No bearer token provided.");
            throw new InvalidOperationException("Not authenticated.  Call AuthenticateAsync first");
        }

        var searchUrl =
            $"https://api.geodoc.no/v1/tenants/DemoProd6/records?$filter=seriesId in ('1099') and gid/any(x:x/gardsnummer eq {gnr} and x/bruksnummer eq {bnr})";
        var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);

        Console.WriteLine($"Sending request to: {searchUrl}");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var jsonRespons = await response.Content.ReadAsStringAsync();

        Console.WriteLine("Received response from the server.");

        var result = JsonConvert.DeserializeObject<dynamic>(jsonRespons);
        Console.WriteLine($"This is the json result: {result}");

        var vedtakDocuments = new List<dynamic>();

        if (result.value.Count == 0)
        {
            Console.WriteLine("No documents found.");
        }

        if (result.value.Count != 0)
        {
            foreach (var doc in result.value)
            {
                bool foundVedtak = false;
                if (doc.metadata.dokumentkategori != null)
                {
                    foreach (var kategori in doc.metadata.dokumentkategori)
                    {
                        if (kategori.value.ToString().Equals("Vedtak", StringComparison.OrdinalIgnoreCase))
                        {
                            foundVedtak = true;
                            vedtakDocuments.Add(doc);
                            Console.WriteLine($"Found 'Vedtak' document with ID: {doc.id}");
                            break;
                        }
                    }
                }

                if (!foundVedtak)
                {
                    Console.WriteLine($"No 'Vedtak' found for document with ID: {doc.id}");
                }
            }
        }

        Console.WriteLine($"Total 'Vedtak' documents found: {vedtakDocuments.Count}");

        return vedtakDocuments;
    }

    public async Task DownloadVedtakDocument(List<dynamic> vedtakDocuments)
    {
        foreach (var doc in vedtakDocuments)
        {
            string documentId = doc.id.ToString();
            Console.WriteLine(documentId);
            string initialUrl = $"https://api.geodoc.no/v1/tenants/DemoProd6/records/{documentId}/download";

            // Fetch containerName and blobName
            var initRequest = new HttpRequestMessage(HttpMethod.Get, initialUrl);
            initRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);

            var initResponse = await _client.SendAsync(initRequest);

            initResponse.EnsureSuccessStatusCode();

            var initResponseBody = await initResponse.Content.ReadAsStringAsync();


            var initResult = JsonConvert.DeserializeObject<dynamic>(initResponseBody);


            string containerName = initResult.containerName;
            string blobName = initResult.blobName;


            // Poll for status
            string statusUrl =
                $"https://api.geodoc.no/v1/tenants/DemoProd6/records/download/status/{containerName}/{blobName}";
            string downloadUri = await PollForStatusAndGetUri(statusUrl);

            if (downloadUri == null)
            {
                Console.WriteLine("No uri fetched");
            }
            else
            {
                Console.WriteLine($"This is the downloadUri: {downloadUri}");
            }

            if (!string.IsNullOrEmpty(downloadUri))
            {
                // Use a well-known location that is mapped correctly in Docker
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string documentPdfPath = Path.Combine(baseDirectory, "DocumentPDF");
                Directory.CreateDirectory(documentPdfPath); // Make sure the directory exists
                string filePath = Path.Combine(documentPdfPath, $"{documentId}.pdf");
        
                // Proceed to download the file
                await DownloadFile(downloadUri, filePath);
            }
            else
            {
                Console.WriteLine($"Failed to obtain download URI for document ID: {documentId}.");
            }
        }
    }

    private async Task<string> PollForStatusAndGetUri(string statusUrl)
    {
        string downloadUri = null;
        int maxAttempts = 5;
        int attempt = 0;
        int pollInterval = 5000; // Interval between polling attempts in milliseconds

        while (attempt < maxAttempts && downloadUri == null)
        {
            attempt++;
            var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUrl);
            statusRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);

            var statusResponse = await _client.SendAsync(statusRequest);
            statusResponse.EnsureSuccessStatusCode();
            var statusResponseBody = await statusResponse.Content.ReadAsStringAsync();
            dynamic statusResult = JsonConvert.DeserializeObject<dynamic>(statusResponseBody);

            if (statusResult.status == "Pending")
            {
                Console.WriteLine($"Document status is still 'Pending'. Attempt {attempt} of {maxAttempts}.");
                await Task.Delay(pollInterval);
            }
            else if (statusResult.status == "Success")
            {
                downloadUri = statusResult.uri;
            }
        }

        return downloadUri;
    }

    private async Task DownloadFile(string url, string filePath)
    {
        // Initiates an asynchronous GET request to the specified URL to download the file
        using (var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            // Throws an exception if the HTTP response status is not successful.
            response.EnsureSuccessStatusCode();

            // Reads the response content as a stream asynchronously.
            using (var stream = await response.Content.ReadAsStreamAsync())
                // Creates a new file stream where the downloaded file will be saved.
            using (var fileStream =
                   new System.IO.FileStream(filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            {
                // Copies the content from the HTTP response stream to the file stream, effectively saving the file locally.
                await stream.CopyToAsync(fileStream);
                Console.WriteLine($"Document saved to {filePath}");
            }
        }
    }
}   

