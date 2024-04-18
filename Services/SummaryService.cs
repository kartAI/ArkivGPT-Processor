using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArkivGPT_Processor.Models;
using Azure;
using Azure.AI.OpenAI;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.WebUtilities;

namespace ArkivGPT_Processor.Services;

public class SummaryService : Summary.SummaryBase
{
    private readonly ILogger<SummaryService> _logger;

    private readonly string _endPoint = File.ReadAllText("../GPT.endpoint");
    private readonly string _apiKey = File.ReadAllText("../GPT.key");
    private GeoDocClient _client;

    public SummaryService(ILogger<SummaryService> logger)
    {
        _logger = logger;
        _client = new GeoDocClient();
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

    public async Task<string> GetGPTResponse(string message)
    {
        OpenAIClient client = new(new Uri(_endPoint), new AzureKeyCredential(_apiKey));
        var text = "";
        try
        {
            var chatCompletionsOptions = new ChatCompletionsOptions()
            {
                DeploymentName = "AI-gutta-pdf-summarizing",
                Messages =
                {
                    new ChatRequestSystemMessage("You are an AI assistant that summarizes PDFs. Summarize the PDF acording to the title or what is the important information in the PDF, the most important is if it is approved, do not include the location. You are to summarize the PDF in less than 15 words on norwegian, but try to keep as much information as you can. Start with the year always, YYYY: Dispensasjon godkjent/avslått for Pdf info"),
                    //new ChatRequestSystemMessage("Summarize the PDF acording to the title or what is the important information in the PDF, the most important is if it is approved, do not include the location."),
                    //new ChatRequestSystemMessage("You are to summarize the PDF in less than 15 words on norwegian. Start with the year, YYYY: Dispensasjon godkjent/avslått for Pdf info"),
                    new ChatRequestUserMessage(message)
                },
                MaxTokens = 100
            };

            Response<ChatCompletions> response = client.GetChatCompletions(chatCompletionsOptions);

            text = response.Value.Choices[0].Message.Content;

            Console.WriteLine(text);
        } catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }

        return text;
    }

    public async Task<string> GetDummyGPTResponse(string message)
    {
        return message;
    }

    public async Task<string> GetOCR(ServerCallContext context, string filename, Ocr.OcrClient client)
    {        
        _logger.LogInformation("Getting reply from ocr");

        var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(120)).Token;
        var clientDisconnectToken = context.CancellationToken;
        var linkTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken, clientDisconnectToken);
        
        try
        {
            var reply = await client.SendOCRAsync(new OcrRequest { Filename = filename }, cancellationToken: linkTokenSource.Token);
            
            return reply.Text;

        }
        catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Cancelled)
        {
            Console.WriteLine("OCR stream cancelled.");
        }
        
        return null;
    }


    private async Task ProcessFileAsync(int fileId, string file, ServerCallContext context, IServerStreamWriter<SummaryReply> responseStream, Ocr.OcrClient client, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting GPT response");
        string ocrText = await GetOCR(context, file, client);

        if (cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Process cancelled before sending to GPT");
            return;
        }
        
        string gptResponse = await GetGPTResponse(ocrText);

        _logger.LogInformation("Sending back response to gateway");
        await responseStream.WriteAsync(new SummaryReply()
        {
            Id = fileId,
            Resolution = gptResponse,
            Document = $"http://localhost/api/document?document={file}"
        }, cancellationToken);
        _logger.LogInformation("Recived response from OCR");
    }

    public override async Task<SummaryReply> SaySummary(
        SummaryRequest request, IServerStreamWriter<SummaryReply> responseStream, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        //_logger.LogInformation("Creating new route to OCR");
        var serverAddress = Environment.GetEnvironmentVariable("GRPC_SERVER_ADDRESS");
        using var channel = GrpcChannel.ForAddress(serverAddress);
        var client = new Ocr.OcrClient(channel);

        // Download documents
        //var records = GetGeoDocRecords(request.Gnr, request.Bnr, request.Snr);
        await _client.AuthenticateAsync();
        var searchResult = await _client.SearchDocumentsAsyncVedtak(request.Gnr, request.Bnr, request.Snr);
        
        Console.WriteLine(searchResult);
        if (searchResult.Count == 0)
        {
            return null;
        }

        await _client.DownloadVedtakDocumentsAsync(searchResult, request.Gnr, request.Bnr, request.Snr);
        
        // Get text from document
        string folder = $"Files/{request.Gnr}-{request.Bnr}-{request.Snr}/";
        var files = Directory.GetFiles(folder);

        try
        {
            var processingTasks = files.Select(file => ProcessFileAsync(Array.IndexOf(files, file), file, context, responseStream, client, cancellationToken)).ToList();
            await Task.WhenAll(processingTasks);
        } catch {
            Console.WriteLine("Error processing files");
        }

        return null;
    }
}
