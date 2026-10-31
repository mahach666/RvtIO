// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace RvtIO.DeflateManaged
{
    /// <summary>
    /// Stream that decompresses Deflate64 data.
    /// Adapted from dotnet/runtime System.IO.Compression.DeflateManaged.
    /// </summary>
    internal sealed class Deflate64Stream : Stream
    {
        internal const int DefaultBufferSize = 8192;

        private Stream? _stream;
        private InflaterManaged _inflater;
        private readonly byte[] _buffer;

        public Deflate64Stream(Stream stream, long uncompressedSize = -1)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanRead)
                throw new ArgumentException("Stream is not readable", nameof(stream));

            _inflater = new InflaterManaged(deflate64: true, uncompressedSize);
            _stream = stream;
            _buffer = new byte[DefaultBufferSize];
        }

        public override bool CanRead => _stream?.CanRead ?? false;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => EnsureNotDisposed();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(new Span<byte>(buffer, offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            EnsureNotDisposed();

            int initialLength = buffer.Length;

            while (true)
            {
                int bytesRead = _inflater.Inflate(buffer);
                buffer = buffer.Slice(bytesRead);

                if (buffer.Length == 0)
                    break;

                if (_inflater.Finished())
                {
                    Debug.Assert(_inflater.AvailableOutput == 0);
                    break;
                }

                int bytes = _stream!.Read(_buffer, 0, _buffer.Length);
                if (bytes <= 0)
                    break;
                if (bytes > _buffer.Length)
                    throw new InvalidDataException("Invalid data");

                _inflater.SetInput(_buffer, 0, bytes);
            }

            return initialLength - buffer.Length;
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("Cannot write to decompression stream");

        private void EnsureNotDisposed()
        {
            ObjectDisposedException.ThrowIf(_stream is null, this);
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing && _stream != null)
                    _stream.Dispose();
            }
            finally
            {
                _stream = null!;
                _inflater = null!;
                base.Dispose(disposing);
            }
        }
    }
}
