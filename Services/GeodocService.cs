using System.Net;
using Azure.Core;
using Newtonsoft.Json;
using JsonConverter = System.Text.Json.Serialization.JsonConverter;

namespace ArkivGPT_Processor.Services;

public class GeoDocClient
{
    // HttpClient is intended to be instantiated once and re-used throughout the life of an application.
    private readonly HttpClient _client = new HttpClient();
    // Authentication token for API access.
    private string _bearerToken = "";
    // Client ID and secret for obtaining the bearer token, read from files.
    private readonly string _clientID = File.ReadAllText("../GeoDoc.clientid");
    private readonly string _secretKey = File.ReadAllText("../GeoDoc.key");
    // The scope of the authentication request.
    private string scope = "https://braarkivb2cprod.onmicrosoft.com/app-api-prod/.default";

    public async Task AuthenticateAsync()
    {
        var tokenEndpoint = "https://login.microsoftonline.com/braarkivb2cprod.onmicrosoft.com/oauth2/v2.0/token";
        var requestBody = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _clientID),
            new KeyValuePair<string, string>("scope", scope),
            new KeyValuePair<string, string>("client_secret", _secretKey),
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

    public async Task<List<dynamic>> SearchDocumentsAsyncVedtak(int gnr, int bnr, int snr)
    {
        if (string.IsNullOrWhiteSpace(_bearerToken))
        {
            Console.WriteLine("No bearer token provided.");
            throw new InvalidOperationException("Not authenticated.  Call AuthenticateAsync first");
        }
        
        // Construct the URL for the API call. This URL queries the GeoDoc service for records 
        var searchUrl =
            $"https://api.geodoc.no/v1/tenants/DemoProd6/records?$filter=seriesId in ('1099') and gid/any(x:x/gardsnummer eq {gnr} and x/bruksnummer eq {bnr} and x/seksjonsnummer eq {snr})";
        
        
        
        var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);

        var response = await _client.SendAsync(request);
        
        Console.WriteLine($"this is the respons: {response.Content}");
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Request failed with status code: {response.StatusCode} and reason: {response.ReasonPhrase}");
            return new List<dynamic>();
        }

        var jsonRespons = await response.Content.ReadAsStringAsync();

        Console.WriteLine("Received response from the server.");
        var result = JsonConvert.DeserializeObject<dynamic>(jsonRespons);
       
        //Console.WriteLine($"This is the json result: {result}");

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

    public async Task DownloadVedtakDocument(List<dynamic> vedtakDocuments, int gnr, int bnr, int snr)
    {
        foreach (var doc in vedtakDocuments)
        {
            string documentId = doc.id.ToString();
            Console.WriteLine($"Initializing download for document ID: {documentId}");
            string initialUrl = $"https://api.geodoc.no/v1/tenants/DemoProd6/records/{documentId}/download";

            // Fetch containerName and blobName
            var initRequest = new HttpRequestMessage(HttpMethod.Get, initialUrl);
            initRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);
            
            var initResponse = await _client.SendAsync(initRequest);

            if (initResponse.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine( $"Document ID : {documentId} not found. Skipping");
                continue;
            }
            else if (!initResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to initiate download for Document ID: {documentId}. Status Code: {initResponse.StatusCode}");
                continue; 

            }

            var initResponseBody = await initResponse.Content.ReadAsStringAsync();
            var initResult = JsonConvert.DeserializeObject<dynamic>(initResponseBody);


            string containerName = initResult.containerName;
            string blobName = initResult.blobName;
            
            // Poll for status
            string statusUrl =
                $"https://api.geodoc.no/v1/tenants/DemoProd6/records/download/status/{containerName}/{blobName}";
            
            // log polling
            Console.WriteLine($"Polling for status of document ID: {documentId}");
            string downloadUri = await PollForStatusAndGetUri(statusUrl);

            if (downloadUri == null)
            {
                Console.WriteLine("No uri fetched");
            }
            else
            {
                Console.WriteLine($"This is the downloadUri: {downloadUri}");
            }
            
            // Download document if if it finds a downloadURI
            if (!string.IsNullOrEmpty(downloadUri))
            {
                Console.WriteLine($"Download URI received for document ID: {documentId}. Proceeding with download...");
                string targetDirectory = $"/processor/Files/{gnr}-{bnr}-{snr}";
                Console.WriteLine($"Download directory has been created {targetDirectory}");
                Directory.CreateDirectory(targetDirectory);

                string filePath = Path.Combine(targetDirectory, $"{documentId}.pdf");
                await DownloadFile(downloadUri, filePath, documentId);

                Console.WriteLine($"Download completed for document ID: {documentId}");
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
        int pollInterval = 5000; 

        while (attempt < maxAttempts && downloadUri == null)
        {
            attempt++;
            var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUrl);
            statusRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);

            var statusResponse = await _client.SendAsync(statusRequest);
            
            if (!statusResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to poll status: {statusResponse.StatusCode}. Response: {await statusResponse.Content.ReadAsStringAsync()}");
                break; // Exit the loop if the status request itself fails
            }
            
            var statusResponseBody = await statusResponse.Content.ReadAsStringAsync();
            dynamic statusResult = JsonConvert.DeserializeObject<dynamic>(statusResponseBody);
    
            // Check the document status in the response
            if (statusResult.status == "Pending")
            {
                Console.WriteLine($"Document status is 'Pending'. Waiting for {pollInterval / 1000} seconds before retrying.");
                await Task.Delay(pollInterval); // Wait before polling again
            }
            else if (statusResult.status == "Accepted")
            {
                Console.WriteLine($"Document processing is 'Accepted'. Waiting for {pollInterval / 1000} seconds before retrying.");
                await Task.Delay(pollInterval); // Specific handling for 'Accepted', similar to 'Pending'
            }
            else if (statusResult.status == "Success")
            {
                downloadUri = statusResult.uri;
                Console.WriteLine("Document is ready for download.");
            }
            else
            {
                Console.WriteLine($"Document status is '{statusResult.status}'. Unable to download.");
                break; // Exit if status is neither 'Pending', 'Accepted', nor 'Success'
            }
        }

        return downloadUri;
    }

    private async Task DownloadFile(string url, string filePath, string documentId)
    {
        if (File.Exists(filePath))
        {
            Console.WriteLine($"File already exists at {filePath}. Skipping download.");
            return;
        }

        HttpResponseMessage response = null;
        try
        {
            response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            
            //check statuscode
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"The file at {url} was not found (404). Skipping download.");
                return;
            }
            else if (!response.IsSuccessStatusCode)
            {
                // Log any other HTTP error status codes and skip download.
                Console.WriteLine($"Failed to download the file from {url}. Status code: {response.StatusCode}, Reason: {response.ReasonPhrase}.");
                return;
            }

            // At this point, we have a success status code, so proceed with reading and saving the file.
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                await stream.CopyToAsync(fileStream);
                Console.WriteLine($"Document saved to {filePath}");
            }
        }
        catch (Exception ex)
        {
            // This block will handle exceptions thrown by other issues, such as network connectivity errors, etc.
            Console.WriteLine($"An unexpected error occurred while trying to download the file from {url}: {ex.Message}");
        }
        finally
        {
            response?.Dispose(); // Ensure the HttpResponseMessage is disposed of to free resources.
        }
    }


}