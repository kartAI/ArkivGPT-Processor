namespace ArkivGPT_Processor.Interfaces;

public interface IArchiveController
{

    /// <summary>
    /// Searches the archive for documents with correct cadastral number
    /// </summary>
    /// <param name="gnr">Gårdsnummer</param>
    /// <param name="bnr">Bruksnummer</param>
    /// <param name="snr">Seksjonsnummer</param>
    /// <returns>A list of found documents, or empty if none are found</returns>
    Task<List<dynamic>> SearchDocumentsAsync(int gnr, int bnr, int snr);
    
    /// <summary>
    /// Downloads the found documents
    /// </summary>
    /// <param name="documents">The list of documents to download</param>
    /// <param name="targetDir">The directory to download them to</param>
    /// <returns>Void</returns>
    Task DownloadDocumentsAsync(List<dynamic> documents, string targetDir);
}