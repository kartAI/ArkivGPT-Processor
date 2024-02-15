using System.ComponentModel.DataAnnotations;

namespace ArkivGPT_Processor.Models;

public record class SummaryElement
{
    public SummaryElement(){}
    
    public SummaryElement(String resolution, String document)
    {
        Resolution = resolution;
        Document = document;
    }

    [Required]
    public String Resolution { get; set; }
    
    [Required]
    public String Document { get; set; }
}