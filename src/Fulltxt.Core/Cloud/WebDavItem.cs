namespace Fulltxt.Core.Cloud;

public sealed record WebDavItem(
    string Href,
    string RelativePath,
    string DisplayName,
    bool IsCollection,
    long ContentLength,
    DateTime LastModifiedUtc,
    string? ETag);
