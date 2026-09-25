namespace Fulltxt.Core.Models;

public sealed class SearchHit
{
    public required long FileId { get; init; }
    public required string FileName { get; init; }
    public required string RelativePath { get; init; }
    public required string SourceDisplayName { get; init; }
    public required SourceType SourceType { get; init; }
    public required string Snippet { get; init; }
}
