using System;
using System.Collections.Generic;
using System.IO;

namespace SplitAndLoad
{
    static partial class FileSystemHelper
    {
        class FileSystemStream : Stream
        {
            private readonly byte[] _buffer;
            private readonly IEnumerator<int> _blocksEnumerator;

            private int _offset = 0;
            private int _size = 0;

            public FileSystemStream(byte[] buffer, IEnumerable<int> blocks)
            {
                _buffer = buffer;
                _blocksEnumerator = blocks.GetEnumerator();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_size <= _offset)
                {
                    if (!_blocksEnumerator.MoveNext())
                        return 0;

                    _offset = 0;
                    _size = _blocksEnumerator.Current;
                }

                int bytesToSend = Math.Min(count, _size - _offset);
                Array.Copy(_buffer, _offset, buffer, offset, bytesToSend);
                _offset += bytesToSend;

                return bytesToSend;
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
}
