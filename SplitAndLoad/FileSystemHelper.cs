using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SplitAndLoad
{
    static partial class FileSystemHelper
    {
        private enum EntryType : byte
        {
            File, Directory
        }

        private const int BufferSize = 1024 * 1024;

        private static IEnumerable<int> ReadEntry(byte[] buffer, string root, BinaryWriter writer, FileSystemInfo entry)
        {
            if (!entry.Exists)
                throw new FileNotFoundException(nameof(entry), entry.FullName);

            if (entry is FileInfo file)
            {
                Console.Write($"0%   - {file.FullName.Substring(root.Length)}");

                writer.Seek(0, SeekOrigin.Begin);
                writer.Write((byte)EntryType.File);
                writer.Write(file.Name);
                writer.Write(file.Length);
                writer.Flush();

                yield return (int)writer.Seek(0, SeekOrigin.Current);

                using (FileStream stream = file.OpenRead())
                {
                    while (stream.Position < stream.Length)
                    {
                        int read = stream.Read(buffer, 0, buffer.Length);
                        Console.Write($"\r{100 * stream.Position / stream.Length}%");
                        yield return read;
                    }
                    Console.WriteLine();
                }
            }
            else if (entry is DirectoryInfo dir)
            {
                var contents = dir.GetFileSystemInfos();

                writer.Seek(0, SeekOrigin.Begin);
                writer.Write((byte)EntryType.Directory);
                writer.Write(dir.Name);
                writer.Write(contents.Length);
                writer.Flush();

                yield return (int)writer.Seek(0, SeekOrigin.Current);

                foreach (var subEntry in contents)
                    foreach (var block in ReadEntry(buffer, root, writer, subEntry))
                        yield return block;
            }
        }

        public static Stream OpenReadEntry(FileSystemInfo entry)
        {
            byte[] buffer = new byte[BufferSize];

            var memory = new MemoryStream(buffer);
            var writer = new BinaryWriter(memory, Encoding.UTF8);

            string root = null;
            if (entry is FileInfo file)
                root = file.Directory.FullName;
            else if (entry is DirectoryInfo dir)
                root = dir.Parent.FullName;

            root += Path.DirectorySeparatorChar;

            return new FileSystemStream(buffer, ReadEntry(buffer, root, writer, entry));
        }

        public static FileSystemInfo DownloadEntry(Stream stream, DirectoryInfo outputDirectory)
        {
            return DownloadEntry(
                outputDirectory.FullName + Path.DirectorySeparatorChar,
                new BinaryReader(stream, Encoding.UTF8),
                outputDirectory,
                new byte[BufferSize]);
        }

        private static FileSystemInfo DownloadEntry(string root, BinaryReader reader, DirectoryInfo outputDirectory, byte[] buffer)
        {
            var type = (EntryType)reader.ReadByte();

            if (type == EntryType.File)
            {
                string name = reader.ReadString();
                long fileSize = reader.ReadInt64();

                var file = new FileInfo(Path.Combine(outputDirectory.FullName, name));

                Console.Write($"0%   - {file.FullName.Substring(root.Length)}");
                int progressPercent = 0;

                using (FileStream stream = file.OpenWrite())
                {
                    var timer = Stopwatch.StartNew();
                    long timerDataRead = 0;

                    long dataRead = 0;
                    while (dataRead < fileSize)
                    {
                        
                        int readCount = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, fileSize - dataRead));
                        stream.Write(buffer, 0, readCount);

                        if (timer.Elapsed.TotalSeconds > 3)
                        {
                            Console.Title = (timerDataRead / timer.Elapsed.TotalSeconds).FormatBytesCount() + "ps";
                            timerDataRead = 0;
                            timer.Restart();
                        }

                        timerDataRead += readCount;
                        dataRead += readCount;
                        
                        int newProgress = (int)(100 * dataRead / fileSize);
                        if (newProgress > progressPercent)
                        {
                            progressPercent = newProgress;
                            Console.Write($"\r{progressPercent}%");
                        }
                    }
                    Console.WriteLine();
                }

                file.Refresh();
                return file;
            }
            else
            {
                string name = reader.ReadString();
                int contentsCount = reader.ReadInt32();

                var dir = outputDirectory.CreateSubdirectory(name);

                while (contentsCount > 0)
                {
                    DownloadEntry(root, reader, dir, buffer);
                    contentsCount--;
                }

                dir.Refresh();
                return dir;
            }
        }
    }
}
