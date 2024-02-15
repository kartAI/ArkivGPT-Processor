using Azure;
using Azure.AI.OpenAI;
using Grpc.Core;

namespace ArkivGPT_Processor.Services;

public class SummaryService : Summary.SummaryBase
{
    private readonly ILogger<SummaryService> _logger;

    private readonly string _endPoint = File.ReadAllText("../GPT.endpoint");
    private readonly string _apiKey = File.ReadAllText("../GPT.key");

    public SummaryService(ILogger<SummaryService> logger)
    {
        _logger = logger;
    }

    public override Task<SummaryReply> SaySummary(SummaryRequest request, ServerCallContext context)
    {
        OpenAIClient client = new(new Uri(_endPoint), new AzureKeyCredential(_apiKey));

        var chatCompletionsOptions = new ChatCompletionsOptions()
        {
            DeploymentName = "AI-gutta-pdf-summarizing",
            Messages =
            {
                new ChatRequestSystemMessage("You are an AI assistant that summarizes PDFs."),
                new ChatRequestSystemMessage("Summarize the PDF acording to the title or what is the important information in the PDF, the most important is if it is approved, do not include the location."),
                new ChatRequestSystemMessage("You are to summarize the PDF in less than 15 words on norwegian. Start with the year, YYYY: Dispensasjon godkjent/avslått for Pdf info"),
                new ChatRequestUserMessage("By- og miljøutvalget godkjenner deling som omsøkt da det foreligger en klar overvekt av argumenter for å kunne gi dispensasjon fra plankravet og pbl §1-8. Kravet til sandlekeplass gis rettighet til evigvarende kyststi og tilsvarende mulighet for opparbeide en allment tilgjengelig badeplass på eiendommen i tråd med situasjonsplanen som medfølger søknaden. For øvrig gjelder vilkårene skissert i saksfremleggets side 8.")
            },
            MaxTokens = 100
        };

        Response<ChatCompletions> response = client.GetChatCompletions(chatCompletionsOptions);

        Console.WriteLine(response.Value.Choices[0].Message.Content);

        Console.WriteLine();
        
        return Task.FromResult(new SummaryReply
        {
            Resolution = response.Value.Choices[0].Message.Content,
            Document = "http://test.com"
        });
    }
}
