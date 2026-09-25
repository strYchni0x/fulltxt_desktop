namespace Fulltxt.Core.Models;

public sealed class SearchHit
{
    public required long FileId { get; init; }
    public required long SourceId { get; init; }
    public required string FileName { get; init; }
    public required string RelativePath { get; init; }
    public required string SourceDisplayName { get; init; }
    public required SourceType SourceType { get; init; }
    public required string Snippet { get; init; }
    public string? RemoteId { get; init; }
    public string? WebUrl { get; init; }

    public bool IsCloud => SourceType.IsCloud();
    public bool HasWebUrl => !string.IsNullOrEmpty(WebUrl);
}
