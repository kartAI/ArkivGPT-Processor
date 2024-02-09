using Azure.AI.OpenAI;
using Grpc.Core;
using Azure;

namespace GrpcGreeter.Services;

public class GreeterService : Greeter.GreeterBase
{
    private readonly ILogger<GreeterService> _logger;

    private readonly string _endPoint = File.ReadAllText("../GPT.endpoint");
    private readonly string _apiKey = File.ReadAllText("../GPT.key");

    public GreeterService(ILogger<GreeterService> logger)
    {
        _logger = logger;
    }

    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        OpenAIClient client = new(new Uri(_endPoint), new AzureKeyCredential(_apiKey));
        
        var chatCompletionsOptions = new ChatCompletionsOptions()
        {
            DeploymentName = "AI-gutta-pdf-summarizing",
            Messages =
            {
                new ChatRequestSystemMessage("You are a helpful assistant."),
                new ChatRequestUserMessage("Does Azure OpenAI support customer managed keys?"),
                new ChatRequestAssistantMessage("Yes, customer managed keys are supported by Azure OpenAI."),
                new ChatRequestUserMessage("Do other Azure AI services support this too?"),
            },
            MaxTokens = 100
        };

        Response<ChatCompletions> response = client.GetChatCompletions(chatCompletionsOptions);

        Console.WriteLine(response.Value.Choices[0].Message.Content);

        Console.WriteLine();

        return Task.FromResult(new HelloReply
        {
            Message = "Hello me stupid " + request.Name
        });
    }
}
