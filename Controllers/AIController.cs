using ArkivGPT_Processor.Interfaces;
using Azure;
using Azure.AI.OpenAI;

namespace ArkivGPT_Processor.Controllers;

public class AIController : IAIController
{
    private readonly string _endPoint;
    private readonly string _apiKey;
    private readonly string _deploymentName;
    private readonly ILogger<AIController> _logger;

    public AIController(string endPoint, string apiKey, string deploymentName)
    {
        _endPoint = endPoint;
        _apiKey = apiKey;
        _deploymentName = deploymentName;
        _logger = new LoggerFactory().CreateLogger<AIController>();
    }
    
    public async Task<string> GetAIResponse(string fullText)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return "";
        }
        
        OpenAIClient client = new(new Uri(_endPoint), new AzureKeyCredential(_apiKey));
        var text = "";
        try
        {
            var chatCompletionsOptions = new ChatCompletionsOptions()
            {
                DeploymentName = _deploymentName,
                Messages =
                {
                    new ChatRequestSystemMessage("Du er en KI assistent som oppsummerer PDFer. Oppsummer PDFen i henhold til tittel og den viktigste informasjonen i PDFen, den viktigste informasjonen er om søknaden er godkjent. Du skal oppsummere PDFen på under 30 ord. Ekskluder addresse, navn, og GNR/BNR (gårdsnummer/bruksnummer) fra oppsummeringen. Om referanse til personer eller eiendom er viktig for oppsummering erstatt navn med funksjonsnavn som 'søker' eller 'søkers nabo' og adresse med eksempler som 'søkers eiendom' eller 'klagers eiendom'. Begynn alltid med år; ÅÅÅÅ: Dispensasjon godkjent/avslått, <pdf info>"),
                    //new ChatRequestSystemMessage("You are an AI assistant that summarizes PDFs. Summarize the PDF acording to the title or what is the important information in the PDF, the most important is if it is approved. You are to summarize the PDF in norwegian in less than 30 words. Do not include the address or GNR/BNR in the summary. Start with the year always, YYYY: Dispensasjon godkjent/avslått for Pdf info"),
                    //new ChatRequestSystemMessage("Summarize the PDF acording to the title or what is the important information in the PDF, the most important is if it is approved, do not include the location."),
                    //new ChatRequestSystemMessage("You are to summarize the PDF in less than 15 words on norwegian. Start with the year, YYYY: Dispensasjon godkjent/avslått for Pdf info"),
                    new ChatRequestUserMessage(fullText)
                },
                MaxTokens = 100
            };
            

            Response<ChatCompletions> response = await client.GetChatCompletionsAsync(chatCompletionsOptions);

            text = response.Value.Choices[0].Message.Content;

        } catch (Exception e)
        {
            _logger.LogWarning(e.Message);
        }

        return text;
    }
    
    /// <summary>
    /// Returns the provided text as a dummy response
    /// </summary>
    /// <param name="fullText">The dummy text</param>
    /// <returns>The full text as dummy</returns>
    public Task<string> GetDummyGPTResponse(string fullText)
    {
        return Task.FromResult(fullText);
    }
}