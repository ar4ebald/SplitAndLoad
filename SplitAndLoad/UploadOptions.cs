using CommandLine;

namespace SplitAndLoad
{
    [Verb("upload", HelpText = "Split and upload file to VK")]
    class UploadOptions
    {
        [Value(0, MetaName = "path", Required = true, HelpText = "File or folder to upload")]
        public string Path { get; set; }

        [Option('c', "community", Required = false, HelpText = "Communuty address to upload to")]
        public string Community { get; set; }
    }

    [Verb("download", HelpText = "Download ")]
    class DownloadOptions
    {
        [Option('p', "path", Default = ".", HelpText = "Folder, that program will download data to")]
        public string Path { get; set; }

        [Value(0, Required = true, HelpText = "Line, that was sent to you, containing documents ids")]
        public string DownloadString { get; set; }
    }
}