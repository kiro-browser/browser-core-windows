namespace FluxBrowser.Models;

public class DownloadItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public long BytesReceived { get; set; }
    public long TotalBytesToReceive { get; set; }
    public string State { get; set; } = "inprogress";
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? CompletedTime { get; set; }
}
