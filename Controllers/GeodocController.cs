using System.Net;
using ArkivGPT_Processor.Interfaces;
using Azure.Core;
using Newtonsoft.Json;
using JsonConverter = System.Text.Json.Serialization.JsonConverter;

namespace ArkivGPT_Processor.Controllers;

public class GeodocController : IArchiveController
{
    // HttpClient is intended to be instantiated once and re-used throughout the life of an application.
    private readonly HttpClient _client;
    // Authentication token for API access.
    private string _bearerToken = "";
    // Client ID and secret for obtaining the bearer token, read from files.
    private readonly string _clientId = File.ReadAllText("../GeoDoc.clientid");
    private readonly string _secretKey = File.ReadAllText("../GeoDoc.key");
    // The scope of the authentication request.
    private const string Scope = "https://braarkivb2cprod.onmicrosoft.com/app-api-prod/.default";

    private readonly ILogger<GeodocController> _logger;

    public GeodocController()
    {
        _logger = new LoggerFactory().CreateLogger<GeodocController>();
        _client = new HttpClient();
    }

    /// <summary>
    /// Authenticates this client with GeoDoc
    /// </summary>
    /// <exception cref="Exception"></exception>
    public async Task AuthenticateAsync()
    {
        var tokenEndpoint = "https://login.microsoftonline.com/braarkivb2cprod.onmicrosoft.com/oauth2/v2.0/token";
        var requestBody = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _clientId),
            new KeyValuePair<string, string>("scope", Scope),
            new KeyValuePair<string, string>("client_secret", _secretKey),
            new KeyValuePair<string, string>("grant_type", "client_credentials")

        });

        var response = await _client.PostAsync(tokenEndpoint, requestBody);
        response.EnsureSuccessStatusCode();
        var jsonContent = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonContent);

        if (tokenResponse != null && tokenResponse.TryGetValue("access_token", out var value))
        {
            _bearerToken = value;
        }
    }
    
    public async Task<List<dynamic>> SearchDocumentsAsync(int gnr, int bnr, int snr)
    {
        await AuthenticateAsync();

        if (string.IsNullOrWhiteSpace(_bearerToken))
        {
            _logger.LogError("Could not authenticate with GeoDoc");
            return new List<dynamic>();
        }
        
        // Construct the URL for the API call. This URL queries the GeoDoc service for records 
        var searchUrl =
            $"https://api.geodoc.no/v1/tenants/DemoProd6/records?$filter=seriesId in ('1099') and gid/any(x:x/gardsnummer eq {gnr} and x/bruksnummer eq {bnr} and x/seksjonsnummer eq {snr})";
        
        
        
        var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);

        var response = await _client.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError($"Request failed with status code: {response.StatusCode} and reason: {response.ReasonPhrase}");
            return new List<dynamic>();
        }

        var jsonRespons = await response.Content.ReadAsStringAsync();

        var result = JsonConvert.DeserializeObject<dynamic>(jsonRespons);
        
        var vedtakDocuments = new List<dynamic>();

        if (result.value.Count == 0)
        {
            _logger.LogDebug("No documents found.");
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
                            _logger.LogDebug($"Found 'Vedtak' document with ID: {doc.id}");
                            break;
                        }
                    }
                }

                if (!foundVedtak)
                {
                    _logger.LogDebug($"No 'Vedtak' found for document with ID: {doc.id}");
                }
            }
        }

        _logger.LogInformation($"Total 'Vedtak' documents found: {vedtakDocuments.Count}");

        return vedtakDocuments;
    }
    public async Task DownloadDocumentsAsync(List<dynamic> documents, string targetDIr)
    {
        var downloadTasks = new List<Task>();
        foreach (var doc in documents)
        {
            // This now starts the task and immediately continues to the next iteration
            var task = DownloadDocuments(doc, targetDIr);
            downloadTasks.Add(task);
        }

        // Process tasks as they complete
        while (downloadTasks.Any())
        {
            var completedTask = await Task.WhenAny(downloadTasks);
            downloadTasks.Remove(completedTask);
            // Optionally handle result or exception of completedTask here
        }
    }
    
    /// <summary>
    /// Starts the download process and downloads the documents
    /// </summary>
    /// <param name="documents">List of the documents to download</param>
    /// <param name="targetDir">The target directory to download the documents to</param>
    /// <returns></returns>
    private async Task DownloadDocuments(dynamic documents, string targetDir)
    {
        
        string documentId = documents.id.ToString();
        string filePath = Path.Combine(targetDir, $"{documentId}.pdf");

        if (File.Exists(filePath))
        {
            _logger.LogDebug($"File already exists at {filePath}. Skipping download.");
            return;
        }

        string initialUrl = $"https://api.geodoc.no/v1/tenants/DemoProd6/records/{documentId}/download";
        _logger.LogDebug($"Initializing download for document ID: {documentId}");

        // Fetch containerName and blobName
        var initRequest = new HttpRequestMessage(HttpMethod.Get, initialUrl);
        initRequest.Headers.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);
        
        var initResponse = await _client.SendAsync(initRequest);

        if (initResponse.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation( $"Document ID : {documentId} not found. Skipping");
            return;
        }
        
        if (!initResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning($"Failed to initiate download for Document ID: {documentId}. Status Code: {initResponse.StatusCode}");
            return; 
        }

        var responseBody = await initResponse.Content.ReadAsStringAsync();
        var jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseBody);


        string containerName = jsonResponse.containerName;
        string blobName = jsonResponse.blobName;
        
        // Poll for status
        string statusUrl =
            $"https://api.geodoc.no/v1/tenants/DemoProd6/records/download/status/{containerName}/{blobName}";
        
        // log polling
        _logger.LogDebug($"Polling for status of document ID: {documentId}");
        string downloadUri = await PollForStatusAndGetUri(statusUrl);

        if (downloadUri == null)
        {
            _logger.LogWarning($"Failed to obtain download URI for document ID: {documentId}.");
            return;
        }
        
        _logger.LogDebug($"This is the downloadUri: {downloadUri}");
        
        // Download document if if it finds a downloadURI
        _logger.LogInformation($"Download URI received for document ID: {documentId}. Proceeding with download...");
        
        Directory.CreateDirectory(targetDir);

        await DownloadFile(downloadUri, filePath);

        _logger.LogInformation($"Download completed for document ID: {documentId}");
    }

    /// <summary>
    /// Polls GeoDoc for status on the download, and returns the URL
    /// </summary>
    /// <param name="statusUrl">The URL to poll</param>
    /// <returns></returns>
    private async Task<string> PollForStatusAndGetUri(string statusUrl)
    {
        var downloadUrl = string.Empty;
        const int maxAttempts = 10;
        const int pollIntervalMs = 1000;
        const int pollIntervalS = pollIntervalMs / 1000;

        for (var attempts = 0; attempts < maxAttempts; attempts++)
        {
            var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUrl);
            statusRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);

            var statusResponse = await _client.SendAsync(statusRequest);
            
            if (!statusResponse.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to poll status: {statusResponse.StatusCode}. Response: {await statusResponse.Content.ReadAsStringAsync()}");
                break; 
            }
            
            var statusResponseBody = await statusResponse.Content.ReadAsStringAsync();
            var statusResult = JsonConvert.DeserializeObject<dynamic>(statusResponseBody) ?? "";
    
            if (statusResult.status == "Pending")
            {
                _logger.LogDebug($"Document status is 'Pending'. Waiting for {pollIntervalS} seconds before retrying.");
            }
            else if (statusResult.status == "Accepted")
            {
                _logger.LogDebug($"Document processing is 'Accepted'. Waiting for {pollIntervalS} seconds before retrying.");
            }
            else if (statusResult.status == "Success")
            {
                downloadUrl = statusResult.uri;
                _logger.LogInformation("Document is ready for download.");
                break; 
            }
            else
            {
                _logger.LogWarning($"Document status is '{statusResult.status}'. Unable to download.");
                break;
            }
            await Task.Delay(pollIntervalMs); 
        }

        return downloadUrl;
    }

    /// <summary>
    /// Download the document
    /// </summary>
    /// <param name="url">The URL to download the document from</param>
    /// <param name="filePath">The path to download it to</param>
    private async Task DownloadFile(string url, string filePath)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            
            if (!response.IsSuccessStatusCode)
            {
                // Log any other HTTP error status codes and skip download.
                _logger.LogWarning($"Failed to download the file from {url}. Status code: {response.StatusCode}, Reason: {response.ReasonPhrase}.");
                return;
            }

            // Proceed with reading and saving the file.
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await stream.CopyToAsync(fileStream);
            _logger.LogInformation($"Document saved to {filePath}");
            
        }
        catch (Exception ex)
        {
            _logger.LogError($"An unexpected error occurred while trying to download the file from {url}: {ex.Message}");
        }
        finally
        {
            response?.Dispose();
        }
    }


}