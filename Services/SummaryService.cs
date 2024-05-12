using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArkivGPT_Processor.Models;
using Azure;
using Azure.AI.OpenAI;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.WebUtilities;
using ArkivGPT_Processor.Controllers;
using ArkivGPT_Processor.Interfaces;

namespace ArkivGPT_Processor.Services;

public class SummaryService : Summary.SummaryBase
{
    private readonly ILogger<SummaryService> _logger;

    private readonly string _endPoint = File.ReadAllText("../GPT.endpoint");
    private readonly string _apiKey = File.ReadAllText("../GPT.key");
    private readonly string _deploymentName = File.ReadAllText("../GPT.deploymentName");

    private readonly IAIController _aiController;
    private readonly IOCRController _ocrController;
    private readonly IArchiveController _archiveController;
    private readonly string _serverAddress = Environment.GetEnvironmentVariable("GRPC_SERVER_ADDRESS");
    
    public SummaryService(ILogger<SummaryService> logger)
    {
        _logger = logger;

        _aiController = new AIController(_endPoint, _apiKey, _deploymentName);
        _ocrController = new OCRController();
        _archiveController = new GeodocController();
    }


    private async Task ProcessFileAsync(int fileId, string file, ServerCallContext context, IServerStreamWriter<SummaryReply> responseStream, Ocr.OcrClient client, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting OCR response");
        string ocrText = await _ocrController.GetOCR(context, file, client);

        if (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Process cancelled before sending to GPT");
            return;
        }
        
        _logger.LogInformation("Getting GPT response");
        string gptResponse = await _aiController.GetAIResponse(ocrText);

        _logger.LogInformation("Sending back response to gateway");
        await responseStream.WriteAsync(new SummaryReply()
        {
            Id = fileId,
            Resolution = gptResponse,
            Document = $"http://localhost/api/document?document={file}"
        }, cancellationToken);
        _logger.LogInformation("Recived response from OCR");
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
            var processingTasks = files.Select(file => ProcessFileAsync(Array.IndexOf(files, file), file, context, responseStream, client, cancellationToken)).ToList();
            await Task.WhenAll(processingTasks);
        } catch (Exception e){
            _logger.LogError("Error processing files : " + e.Message);
        }

        return null;
    }
}
