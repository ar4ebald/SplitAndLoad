using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

namespace SplitAndLoad
{
    class ConcatenationgStream : Stream
    {
        private readonly HttpClient _client = new HttpClient();
        private readonly Queue<string> _urls;

        private Stream _currentStream;

        public ConcatenationgStream(IEnumerable<string> urls)
        {
            _urls = new Queue<string>(urls);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = -1;

            do
            {
                if (read == 0 || _currentStream == null)
                {
                    _currentStream?.Dispose();

                    if (_urls.Count == 0) return 0;
                    _currentStream = _client.GetStreamAsync(_urls.Dequeue()).Result;
                }

                read = _currentStream.Read(buffer, offset, count);
            } while (read == 0);

            return read;
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}