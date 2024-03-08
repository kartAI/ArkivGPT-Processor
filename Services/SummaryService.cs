using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArkivGPT_Processor.Models;
using Azure;
using Azure.AI.OpenAI;
using Grpc.Core;
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

        var chatCompletionsOptions = new ChatCompletionsOptions()
        {
            DeploymentName = "AI-gutta-pdf-summarizing",
            Messages =
            {
                new ChatRequestSystemMessage("You are an AI assistant that summarizes PDFs. Summarize the PDF acording to the title or what is the important information in the PDF, the most important is if it is approved, do not include the location. You are to summarize the PDF in less than 15 words on norwegian, but try to keep as much information as you can. Start with the year, YYYY: Dispensasjon godkjent/avslått for Pdf info"),
                //new ChatRequestSystemMessage("Summarize the PDF acording to the title or what is the important information in the PDF, the most important is if it is approved, do not include the location."),
                //new ChatRequestSystemMessage("You are to summarize the PDF in less than 15 words on norwegian. Start with the year, YYYY: Dispensasjon godkjent/avslått for Pdf info"),
                new ChatRequestUserMessage(message)
            },
            MaxTokens = 100
        };

        Response<ChatCompletions> response = client.GetChatCompletions(chatCompletionsOptions);

        var text = response.Value.Choices[0].Message.Content;

        Console.WriteLine(text);

        Console.WriteLine();

        return text;
    }

    public async Task<string> GetDummyGPTResponse(string message)
    {
        return message;
    }


    public override async Task<SummaryReply> SaySummary(
        SummaryRequest request, IServerStreamWriter<SummaryReply> responseStream, ServerCallContext context)
    {
        //var records = GetGeoDocRecords(request.Gnr, request.Bnr, request.Snr);
        await _client.AuthenticateAsync();
        var searchResult = await _client.SearchDocumentsAsyncVedtak(request.Gnr, request.Bnr);
        
        Console.WriteLine(searchResult);

        await _client.DownloadVedtakDocument(searchResult);
        
        
        
        
        var records = new List<string>()
        {
            "2014 By- og miljøutvalget godkjenner deling som omsøkt da det foreligger en klar overvekt av argumenter for å kunne gi dispensasjon fra plankravet og pbl §1-8. Kravet til sandlekeplass gis rettighet til evigvarende kyststi og tilsvarende mulighet for opparbeide en allment tilgjengelig badeplass på eiendommen i tråd med situasjonsplanen som medfølger søknaden. For øvrig gjelder vilkårene skissert i saksfremleggets side 8.",
            "PLAN-, BYGG- OG OPPMÅLINGSETATEN Byggesaksavdelingen Flekkerøy Bygg AS Østerøya 39 4625 FLEKKERØY Vår ref.: 201508185-8 /CAB (Bes oppgitt ved henvendelse) Deres ref.: Dato: Kristiansand, 11.10.2016 ANDEM BONA KRISTIANSAND KOMMUNE Skudeviga 33 - 4/10 - Mottatt mangelfull søknad om dispensasjon for utvidelse av brygge Byggeplass: Skudeviga 33 Ansvarlig søker: Flekkerøy Bygg AS Eiendom: Adresse: 4/10 Østerøya 39 4625 FLEKKERØY Tiltakshaver: Olav Øystein Kristoffersen Adresse: Skudeviga 33 4625 FLEKKERØY Tiltakstype/tiltaksart: Brygger /Vesentlig utvidelse Det vises til søknad om dispensasjon og rammetillatelse mottatt 9.7.2015. Vi beklager den lange saksbehandlingstiden da disse sjøbodsakene på Flekkerøy er kompliserte og tidkrevende. Da søknaden gjelder flere tiltak er det av praktiske årsaker opprettet tre saker. En som omfatter bruksendring til kombinert bruk av sjøboden lengst nord på eiendommen, arkivsaksnr. 201508223, en som omfatter utvidelse av brygge, arkivsaksnr. 201508185 og en som omfatter påbygging, fasadeendring og bruksendring til kombinert bruk av sjøboden som er vestvendt, arkivsaksnr. 201508225. Dette brevet gjelder utvidelse av brygge. Etter en gjennomgang av søknaden er det registrert manglende dokumentasjon og den kan ikke behandles før den er komplett. I kommunedelplanens arealdel er det plankrav og området der brygga ligger er avsatt til LNF-formål. Utvidelse av brygge er dermed også i strid med arealformålet. Denne brygga kommer ikke inn under bestemmelsen som gjelder brygger § 3 bokstav G. I tillegg vises til byggeforbudet i 100-metersbeltet til sjøen, plan- og bygningsloven (pbl) § 1-8. Det vises til at det generelle hensynet bak plankravet i kommunedelplanen er at kommunen vil sikre en forsvarlig og gjennomtenkt utvikling av arealutnyttelsen i området. Viktig er ulemper for naboer og allmennhetens tilgang til friluftsområder og sjøen. En bestemmelse om plankrav før tiltak kan gjennomføres, gir styring med den samlede arealutnyttelsen og arealbruken i byggeområdene. Plankravet skal videre sikre en forsvarlig opplysning i saken, bl.a. få frem hvilke konsekvenser en tillatelse vil kunne innebære for utviklingen i området. De berørtes interesser, nasjonal strandsonepolitikk, herunder konsekvensene for allmennheten, friluftsområdene og sjønære arealer, vil ved utarbeidelse av plan bli vurdert i et helhetsperspektiv. Postadresse Kristiansand kommune Byggesaksavdelingen Postboks 417 Lund 4604 KRISTIANSAND S Besøksadresse Rådhusgata 18 Kristiansand Vår saksbehandler Cathrine Bie Telefon +47 38 24 31 96 E-postadresse post.teknisk@kristiansand.kommune.no Webadresse http://www.kristiansand.kommune.no Foretaksregisteret NO963296746 Videre vises til hensynet bak LNF-formålet (landbruks,- natur,- og friluftsområde) og pbl § 1-8 «<Forbud mot tiltak mv. langs sjø og vassdrag>>: Stortinget har gitt 100-metersbeltet langs sjøen en særskilt beskyttelse. Det er et nasjonalt mål at dette området skal bevares som natur- og friluftsområde tilgjengelig for alle, jf. St.meld. nr. 26 (2006- 2007). I 100-metersbeltet skal det derfor tas særlig hensyn til naturmiljø, landskap, friluftsliv og andre allmenne interesser. Nedbygging av strandsonen bør unngås. Det skal svært mye til for å tillate nye byggetiltak i LNF- områder i strandsonen. Følgende mangler er registrert og bes innsendt: Mottatt søknad om dispensasjon er ikke tilstrekkelig begrunnet jamfør nevnte hensyn over. En revidert søknad med relevante begrunnelser på hvorfor hensynene til bestemmelsene ikke blir vesentlig tilsidesatt i dette tilfellet og om fordelene er i overvekt jamfør pbl. §§ 19-1 og 19-2 må innsendes. Saken legges i bero til etterspurt dokumentasjon er mottatt. Manglene bes innsendt snarest mulig og innen 60 dager fra mottakelse av dette brevet. Ved spørsmål til saken kan det tas kontakt med undertegnede på mobil: 480 93 629 eller e-post: cathrine.bie@kristiansand.kommune.no. Med hilsen Cathrine Bie Saksbehandler Dokumentet er godkjent elektronisk og gyldig uten underskrift Kopi: Olav Øystein Kristoffersen, Skudeviga 33, 4625 FLEKKERØY 2"
        };

        foreach (var record in records)
        {
            _logger.LogInformation("Getting GPT response");
            var gptResponse = await GetDummyGPTResponse(record);
            _logger.LogInformation("Sending back response to gateway: " + gptResponse);
            await responseStream.WriteAsync(new SummaryReply()
            {
                Resolution = gptResponse,
                Document = $"http://{gptResponse}.com"
            });
        }

        return null;
    }
}
