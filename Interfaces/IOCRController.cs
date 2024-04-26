using Grpc.Core;

namespace ArkivGPT_Processor.Interfaces;

public interface IOCRController
{
    /// <summary>
    /// Retrieves the scanned text from the document using the OCR module
    /// </summary>
    /// <param name="context"></param>
    /// <param name="filename"></param>
    /// <param name="client"></param>
    /// <returns></returns>
    Task<string> GetOCR(ServerCallContext context, string filename, Ocr.OcrClient client);

}