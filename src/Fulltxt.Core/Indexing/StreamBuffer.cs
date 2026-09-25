namespace Fulltxt.Core.Indexing;

internal static class StreamBuffer
{
    /// <summary>Liefert einen durchsuchbaren Stream. Netzwerk-Streams werden dafür in den Speicher gepuffert
    /// (Dateigröße ist vorab auf das Indexierungs-Limit begrenzt); der Aufrufer gibt das Ergebnis frei.</summary>
    public static Stream EnsureSeekable(Stream source)
    {
        if (source.CanSeek) return new NonOwningStream(source);

        var buffer = new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>Verhindert, dass Bibliotheken den vom Aufrufer besessenen Stream schließen.</summary>
    private sealed class NonOwningStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
