using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Ponango.Inertia;

/// <summary>
/// Wraps the response body for one request and records whether anything was written to it, so the middleware can
/// tell an empty response from one whose body is still buffered further out (response compression, for example).
/// A body of <c>""</c> counts as empty; any byte, a started response, or a sent file does not.
/// </summary>
internal sealed class ResponseWriteTracker : IHttpResponseBodyFeature
{
    private readonly IHttpResponseBodyFeature _inner;
    private Stream? _stream;
    private PipeWriter? _writer;

    private ResponseWriteTracker(IHttpResponseBodyFeature inner) => _inner = inner;

    /// <summary>Whether a body was written or the response was started.</summary>
    public bool HasWritten { get; private set; }

    /// <summary>Replaces the request's body feature with a tracker. Dispose the result to restore the original.</summary>
    public static Scope Track(HttpContext context)
    {
        var original = context.Features.Get<IHttpResponseBodyFeature>();
        if (original == null)
            return new Scope(context, null, null);

        var tracker = new ResponseWriteTracker(original);
        context.Features.Set<IHttpResponseBodyFeature>(tracker);
        return new Scope(context, original, tracker);
    }

    internal void MarkWritten() => HasWritten = true;

    public Stream Stream => _stream ??= new TrackingStream(_inner.Stream, this);

    public PipeWriter Writer => _writer ??= new TrackingPipeWriter(_inner.Writer, this);

    public void DisableBuffering() => _inner.DisableBuffering();

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        MarkWritten();
        return _inner.StartAsync(cancellationToken);
    }

    public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
    {
        MarkWritten();
        return _inner.SendFileAsync(path, offset, count, cancellationToken);
    }

    public Task CompleteAsync() => _inner.CompleteAsync();

    internal readonly struct Scope : IDisposable
    {
        private readonly HttpContext _context;
        private readonly IHttpResponseBodyFeature? _original;

        public Scope(HttpContext context, IHttpResponseBodyFeature? original, ResponseWriteTracker? tracker)
        {
            _context = context;
            _original = original;
            Tracker = tracker;
        }

        public ResponseWriteTracker? Tracker { get; }

        public void Dispose()
        {
            // Put the original feature back only if nothing further in replaced the tracker in the meantime.
            if (_original != null && ReferenceEquals(_context.Features.Get<IHttpResponseBodyFeature>(), Tracker))
                _context.Features.Set(_original);
        }
    }

    private sealed class TrackingStream : Stream
    {
        private readonly Stream _inner;
        private readonly ResponseWriteTracker _tracker;

        public TrackingStream(Stream inner, ResponseWriteTracker tracker)
        {
            _inner = inner;
            _tracker = tracker;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count > 0) _tracker.MarkWritten();
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (!buffer.IsEmpty) _tracker.MarkWritten();
            _inner.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            _tracker.MarkWritten();
            _inner.WriteByte(value);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (count > 0) _tracker.MarkWritten();
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!buffer.IsEmpty) _tracker.MarkWritten();
            return _inner.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class TrackingPipeWriter : PipeWriter
    {
        private readonly PipeWriter _inner;
        private readonly ResponseWriteTracker _tracker;

        public TrackingPipeWriter(PipeWriter inner, ResponseWriteTracker tracker)
        {
            _inner = inner;
            _tracker = tracker;
        }

        public override void Advance(int bytes)
        {
            if (bytes > 0) _tracker.MarkWritten();
            _inner.Advance(bytes);
        }

        public override Memory<byte> GetMemory(int sizeHint = 0) => _inner.GetMemory(sizeHint);
        public override Span<byte> GetSpan(int sizeHint = 0) => _inner.GetSpan(sizeHint);
        public override void CancelPendingFlush() => _inner.CancelPendingFlush();
        public override void Complete(Exception? exception = null) => _inner.Complete(exception);
        public override ValueTask CompleteAsync(Exception? exception = null) => _inner.CompleteAsync(exception);

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
            => _inner.FlushAsync(cancellationToken);

        public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        {
            if (!source.IsEmpty) _tracker.MarkWritten();
            return _inner.WriteAsync(source, cancellationToken);
        }

        public override bool CanGetUnflushedBytes => _inner.CanGetUnflushedBytes;
        public override long UnflushedBytes => _inner.UnflushedBytes;
    }
}
