using ArkivGPT_Processor.Interfaces;
using ArkivGPT_Processor.Services;
using Grpc.Core;

namespace ArkivGPT_Processor.Controllers;

public class OCRController : IOCRController
{
    
    private readonly ILogger<OCRController> _logger;

    public OCRController()
    {
        _logger = new LoggerFactory().CreateLogger<OCRController>();
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
            return "OCR stream cancelled.";
        }
    }
}