using ArkivGPT_Processor.Interfaces;
using ArkivGPT_Processor.Services;
using Grpc.Core;
using Polly.CircuitBreaker;

namespace ArkivGPT_Processor.Controllers;

public class OCRController : IOCRController
{
    
    private readonly ILogger<OCRController> _logger;
    private AsyncCircuitBreakerPolicy _circuitBreakerPolicy;

    public OCRController(AsyncCircuitBreakerPolicy circuitBreakerPolicy)
    {
        _logger = new LoggerFactory().CreateLogger<OCRController>();
        _circuitBreakerPolicy = circuitBreakerPolicy;
    }
    
    public async Task<string> GetOCR(ServerCallContext context, string filename, Ocr.OcrClient client)
    {        
        _logger.LogInformation("Getting reply from ocr");

        var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(120)).Token;
        var clientDisconnectToken = context.CancellationToken;
        var linkTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken, clientDisconnectToken);
        
        try
        {
            var reply = await _circuitBreakerPolicy.ExecuteAsync( async () => 
                await client.SendOCRAsync(new OcrRequest { Filename = filename }, cancellationToken: linkTokenSource.Token)
            );
            
            return reply.Text;

        }
        catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Cancelled)
        {
            Console.WriteLine("OCR stream cancelled.");
            return null;
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError($"OCR request failed due to broken circuit: {ex.Message}");
            return "Circuit Breaker is open. Retrying later might succeed.";
        }
    }
}