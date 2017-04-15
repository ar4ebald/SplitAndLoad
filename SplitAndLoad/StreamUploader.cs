using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SplitAndLoad
{
    static class StreamUploader
    {
        private static readonly Random Random = new Random();

        private static string RandomHexString(int length)
        {
            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                int digit = Random.Next(16);
                chars[i] = (char)(digit < 10 ? digit + '0' : digit - 10 + 'A');
            }
            return new string(chars);
        }

        public static async Task<(long totalSize, IReadOnlyList<string> parts)> UploadAsync(Client vkClient, Stream source, string namePrefix, string url)
        {
            const int partSize = 200 * 1000 * 1000; // 200MB
            //const int partSize = 40 * 1024;

            var partsInfoStrings = new List<string>();
            long totalSize = 0;

            using (var client = new HttpClient())
            {
                int partIndex = 0;
                int uploadedSize;

                do
                {
                    var form = new MultipartFormDataContent
                    {
                        {
                            new StreamContent(new PartialReader(source, partSize)),
                            "file",
                            "input.txt"
                        }
                    };

                    HttpResponseMessage response = await client.PostAsync(url, form);
                    response.EnsureSuccessStatusCode();

                    var uploadResult = JToken.Parse(await response.Content.ReadAsStringAsync());

                    var doc = (await vkClient.GetAsync("docs.save", new
                    {
                        File = uploadResult.Value<string>("file"),
                        Title = $"{namePrefix} - part:{partIndex++} {RandomHexString(32)}.txt"
                    }))[0];

                    partsInfoStrings.Add($"{doc["owner_id"]}_{doc["id"]}");

                    uploadedSize = doc.Value<int>("size");
                    totalSize += uploadedSize;

                } while (uploadedSize == partSize);
            }

            return (totalSize, partsInfoStrings);
        }

        class PartialReader : Stream
        {
            private readonly Stream _baseStream;
            private readonly int _totalBytesToRead;

            private int _bytesRead = 0;

            public PartialReader(Stream baseStream, int bytesToRead)
            {
                _baseStream = baseStream;
                _totalBytesToRead = bytesToRead;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int bytesToRead = Math.Min(count, _totalBytesToRead - _bytesRead);
                int read = _baseStream.Read(buffer, offset, bytesToRead);
                _bytesRead += read;
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
}
