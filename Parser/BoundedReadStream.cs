namespace WebParser.Parser
{
    /// <summary>
    /// Стрим с ограничением суммарного объёма прочитанных байтов: сначала отдаёт префикс из буфера,
    /// затем читает изложенный поток и выбрасывает <see cref="PageTooLargeException"/> при превышении лимита.
    /// </summary>
    internal sealed class BoundedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly byte[] _prefix;
        private readonly int _prefixLen;
        private readonly long _maxBytes;
        private int _prefixPos;
        private long _total;

        /// <summary>
        /// Создаёт стрим, повторяющий префикс из <paramref name="prefix"/> и читающий остаток из <paramref name="inner"/>.
        /// </summary>
        /// <param name="inner">Изложенный поток с телом страницы.</param>
        /// <param name="prefix">Буфер с уже прочитанным префиксом тела.</param>
        /// <param name="prefixLen">Число значимых байтов в <paramref name="prefix"/>.</param>
        /// <param name="total">Уже прочитанный объём данных в байтах.</param>
        /// <param name="maxBytes">Максимальный суммарный объём чтения в байтах.</param>
        public BoundedReadStream(Stream inner, byte[] prefix, int prefixLen, long total, long maxBytes)
        {
            _inner = inner;
            _prefix = prefix;
            _prefixLen = prefixLen;
            _total = total;
            _maxBytes = maxBytes;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_prefixPos < _prefixLen)
            {
                int n = Math.Min(buffer.Length, _prefixLen - _prefixPos);
                _prefix.AsMemory(_prefixPos, n).CopyTo(buffer);
                _prefixPos += n;
                _total += n;
                EnsureWithinLimit();
                return n;
            }

            int read = await _inner.ReadAsync(buffer, cancellationToken);
            _total += read;
            EnsureWithinLimit();
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private void EnsureWithinLimit()
        {
            if (_total > _maxBytes)
            {
                throw new PageTooLargeException((int)_maxBytes);
            }
        }
    }
}