using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.IO;
using System.Collections.Generic;
using ArkivGPT_Processor.Models;
using Azure;
using Azure.AI.OpenAI;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.WebUtilities;
using ArkivGPT_Processor.Controllers;
using ArkivGPT_Processor.Interfaces;
using Newtonsoft.Json;

namespace ArkivGPT_Processor.Services;

public class SummaryService : Summary.SummaryBase
{
    private readonly bool HashMapEnabled = true // Quick way for someone developing to disable the hashmap check so that the chatgpt output can be tested more directly
    private readonly ILogger<SummaryService> _logger;

    private readonly string _endPoint = File.ReadAllText("../GPT.endpoint");
    private readonly string _apiKey = File.ReadAllText("../GPT.key");
    private readonly string _deploymentName = File.ReadAllText("../GPT.deploymentName");

    private readonly IAIController _aiController;
    private readonly IOCRController _ocrController;
    private readonly IArchiveController _archiveController;
    private readonly string _serverAddress = Environment.GetEnvironmentVariable("GRPC_SERVER_ADDRESS");

    private readonly string jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "GnrBnrSnr-ProcessedValuesHashmap.json");
    
    public SummaryService(ILogger<SummaryService> logger)
    {
        _logger = logger;

        _aiController = new AIController(_endPoint, _apiKey, _deploymentName);
        _ocrController = new OCRController();
        _archiveController = new GeodocController();
    }

    private FileStream WaitForFile (string fullPath, FileMode mode, FileAccess access, FileShare share)
            {
                for (int numTries = 0; numTries < 10; numTries++) {
                    FileStream fs = null;
                    try {
                        fs = new FileStream (fullPath, mode, access, share);
                        return fs;
                    }
                    catch (Exception e) {
                        _logger.LogError("error: " + e.Message);
                        if (fs != null) {
                            fs.Dispose ();
                        }
                        Thread.Sleep (500);
                    }
                }

                return null;
            }

    private bool CheckHashMapForExisting(int fileId, int Gnr, int Bnr, int Snr)
    /// Takes ID of file in Gnr/Bnr/Snr dir(fileId), Gårdsnummer(Gnr), Bruksnummer(Bnr). Returns true if hash map exists and contains entry, else false
    {        
        // check if file exists, if not return false
        if (!System.IO.File.Exists(jsonPath))
        {
            return false;
        }

        // load json as dict and check if Gnr/Bnr/Snr/fId are in dict return false if they are not.
        string json = File.ReadAllText(jsonPath);
        Dictionary<string, string> dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
        if (!dictionary.ContainsKey($"{Gnr}-{Bnr}-{Snr}-{fileId}"))
        {
            return false;
        }

        if (!HashMapEnabled)
        {
            return false;
        }

        return true;
    }

    private async Task ProcessFileAsync(int fileId, string file, ServerCallContext context, IServerStreamWriter<SummaryReply> responseStream, Ocr.OcrClient client, CancellationToken cancellationToken, int Gnr, int Bnr, int Snr)
    {
        string gptResponse;
        // use CheckHashMap to maybe skip expensive processes
        if (!CheckHashMapForExisting(fileId, Gnr, Bnr, Snr))
        {
        _logger.LogInformation("Getting OCR response");
        string ocrText = await _ocrController.GetOCR(context, file, client);
        _logger.LogInformation("Received response from OCR");


        if (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Process cancelled before sending to GPT");
            return;
        }
        
        _logger.LogInformation("Getting GPT response");
        gptResponse = await _aiController.GetAIResponse(ocrText);

        // update hashmap
        //var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "GnrBnrSnr-ProcessedValuesHashmap.json");

        // if file does not exist creates a dictionary with the first entry and creates new file with that as content 
        if (!System.IO.File.Exists(jsonPath))
        {
            // create dictionary and create json from it since file does not exist
            Dictionary<string, string> dictionary = new Dictionary<string, string>()
            {
                {$"{Gnr}-{Bnr}-{Snr}-{fileId}", gptResponse}
            };
            string json = JsonConvert.SerializeObject(dictionary, Formatting.Indented);
            File.WriteAllText(jsonPath, json);
            
            _logger.LogInformation($"json hashmap initialized with first entry gnr:{Gnr} bnr:{Bnr} snr:{Snr} file id:{fileId} at {jsonPath}");
        } else {
            // load hashmap from json as dict, add new key/value pair, save to json, lock file until new entry has been saved to it.

            
            using (FileStream fs = WaitForFile(jsonPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                using (StreamReader reader = new StreamReader(fs))
                {
                    string json = reader.ReadToEnd();
                    Dictionary<string, string> dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    dictionary[$"{Gnr}-{Bnr}-{Snr}-{fileId}"] = gptResponse;

                    // Reset the stream length and write the updated content
                    fs.SetLength(0);

                    using (StreamWriter writer = new StreamWriter(fs))
                    {
                        string updatedJson = JsonConvert.SerializeObject(dictionary, Formatting.Indented);
                        writer.Write(updatedJson);
                    }
                }
            }
            _logger.LogInformation($"json hashmap appended with entry gnr:{Gnr} bnr:{Bnr} snr:{Snr} file id:{fileId} at {jsonPath}");
        }

        } else {
            // load hashmap from json as dict, find value according to key and make gptResponse that value
            _logger.LogInformation("Skipping OCR and GPT in favor of pre-computed answer from hashmap");
            string json = File.ReadAllText(jsonPath);
            Dictionary<string, string> dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            gptResponse = dictionary[$"{Gnr}-{Bnr}-{Snr}-{fileId}"];
        }

        _logger.LogInformation("Sending back response to gateway");
        await responseStream.WriteAsync(new SummaryReply()
        {
            Id = fileId,
            Resolution = gptResponse,
            Document = $"http://localhost/api/document?document={file}"
        }, cancellationToken);
    }
    

    public override async Task<SummaryReply?> SaySummary(
        SummaryRequest request, IServerStreamWriter<SummaryReply> responseStream, ServerCallContext context)
    {
        // Download documents
        var searchResult = await _archiveController.SearchDocumentsAsync(request.Gnr, request.Bnr, request.Snr);
        
        if (searchResult.Count == 0)
        {
            return null;
        }
        
        var targetDirectory = $"Files/{request.Gnr}-{request.Bnr}-{request.Snr}/";

        await _archiveController.DownloadDocumentsAsync(searchResult, targetDirectory);

        try
        {
            var files = Directory.GetFiles(targetDirectory);
            using var channel = GrpcChannel.ForAddress(_serverAddress);
            var cancellationToken = context.CancellationToken;
            var client = new Ocr.OcrClient(channel);
            var processingTasks = files.Select(file => ProcessFileAsync(Array.IndexOf(files, file), file, context, responseStream, client, cancellationToken, request.Gnr, request.Bnr, request.Snr)).ToList();
            await Task.WhenAll(processingTasks);
        } catch (Exception e){
            _logger.LogError("Error processing files : " + e.Message);
        }

        return null;
    }
}
