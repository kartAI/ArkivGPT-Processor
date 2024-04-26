namespace ArkivGPT_Processor.Interfaces;

public interface IAIController
{
    /// <summary>
    /// Gets a summary from the AI
    /// </summary>
    /// <param name="fullText">The text to make a summary of</param>
    /// <returns>A summary of the full text</returns>
    Task<string> GetAIResponse(string fullText);
}