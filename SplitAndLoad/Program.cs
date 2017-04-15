using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommandLine;
using Newtonsoft.Json.Linq;

namespace SplitAndLoad
{
    static class Program
    {
        private const string ApplicationScope = "messages,docs";
        private const int ClientId = 4989758;

        private static readonly string CredentialsPath;

        static Program()
        {
            const string credentialsFileName = "credentials.json";

            string peFileName = Assembly.GetEntryAssembly().Location;
            string peDirectory = new FileInfo(peFileName).DirectoryName;

            CredentialsPath = Path.Combine(peDirectory, credentialsFileName);
        }

        public static string FormatBytesCount(this double count)
        {
            const int step = 1024;
            const double limit = 999.5;

            if (count < step)
                return $"{count:G3}B";

            count /= limit;
            if (count < step)
                return $"{count:G3}KB";

            count /= limit;
            if (count < step)
                return $"{count:G3}MB";

            count /= limit;
            return $"{count:G3}GB";
        }

        private static void Main(string[] args)
        {
            var task = Parser.Default.ParseArguments<AuthenticationOptions, UploadOptions, DownloadOptions>(args)
                .MapResult(
                    async (AuthenticationOptions o) => await AuthenticateAsync(o),
                    async (UploadOptions o) => await UploadAsync(o),
                    async (DownloadOptions o) => await DownloadAsync(o),
                    err => Task.CompletedTask);

            task.GetAwaiter().GetResult();
        }

        private static async Task<(Client client, string errorMessage)> LoadClient(bool checkClient = true)
        {

            if (!File.Exists(CredentialsPath))
                return (null, "You are not authenticated yet. Call auth first.");

            var client = JObject.Parse(File.ReadAllText(CredentialsPath)).ToObject<Client>();

            if (checkClient && !await client.CheckTokenIsValidAsync())
            {
                File.Delete(CredentialsPath);
                return (null, "Authentication token is expired.");
            }

            return (client, null);
        }

        private static async Task AuthenticateAsync(AuthenticationOptions options)
        {
            var client = await Client.AuthenticateAsync(options.UserName, options.Password, ClientId, ApplicationScope);

            if (client == null)
            {
                Console.WriteLine("Invalid login or password");
                return;
            }

            File.WriteAllText(CredentialsPath, JObject.FromObject(client).ToString());
            var user = (await client.GetAsync("users.get", new { Fields = "domain" })).First;
            Console.WriteLine($"Successfully logged in as {user["first_name"]} {user["last_name"]} [https://vk.com/{user["domain"]}]");
        }

        private static async Task UploadAsync(UploadOptions options)
        {
            FileSystemInfo entryInfo;

            if (File.Exists(options.Path))
                entryInfo = new FileInfo(options.Path);
            else if (Directory.Exists(options.Path))
                entryInfo = new DirectoryInfo(options.Path);
            else
            {
                Console.WriteLine("Cannot find specified file or directory");
                return;
            }

            Console.WriteLine("Validating API access...");
            var (vkClient, errorMsg) = await LoadClient();

            if (errorMsg != null)
            {
                Console.WriteLine(errorMsg);
                return;
            }


            var requestArgs = new { GroupId = 0L };
            if (options.Community != null)
            {
                Console.WriteLine($"Searching \"{options.Community}\" community...");
                var groups = await vkClient.GetAsync("groups.getById", new { GroupIds = options.Community });
                requestArgs = new { GroupId = groups[0].Value<long>("id") };
            }

            Console.WriteLine("Getting upload server...");
            var response = await vkClient.GetAsync("docs.getUploadServer", requestArgs);
            var server = response.Value<string>("upload_url");


            long totalSize;
            IReadOnlyList<string> parts;

            Console.WriteLine("Uploading files...");
            using (var file = FileSystemHelper.OpenReadEntry(entryInfo))
                (totalSize, parts) = await StreamUploader.UploadAsync(vkClient, file, entryInfo.Name, server);


            string message = $"{entryInfo.Name}" +
                             $"\nSize: {FormatBytesCount(totalSize)}" +
                             $"\n💿{string.Join(",", parts)}💿";

            Console.WriteLine("Sending notification...");
            await vkClient.GetAsync("messages.send", new
            {
                UserId = vkClient.IdentityId,
                RandomId = new Random().Next(),
                Message = message
            });

            Console.WriteLine($"Done!\n{message}");
        }

        private static async Task DownloadAsync(DownloadOptions options)
        {
            var outputDirectory = new DirectoryInfo(options.Path);

            if (!outputDirectory.Exists)
            {
                Console.WriteLine("Specified path don't exists");
                return;
            }

            var parts = Regex.Matches(options.DownloadString, @"-?\d+_\d+");
            if (parts.Count == 0)
            {
                Console.WriteLine("Can't fetch documents from arguments");
                return;
            }

            Console.WriteLine("Validating API access...");
            var (vkClient, errorMsg) = await LoadClient();

            if (errorMsg != null)
            {
                Console.WriteLine(errorMsg);
                return;
            }

            Console.WriteLine("Fetching documents...");
            var docs = await vkClient.GetAsync("docs.getById", new
            {
                Docs = string.Join(",", parts.Cast<Match>())
            });
            var urls = docs.Select(i => i.Value<string>("url"));

            Console.WriteLine("Downloading files...");
            using (var stream = new ConcatenationgStream(urls))
                FileSystemHelper.DownloadEntry(stream, outputDirectory);

            Console.WriteLine("Done!");
        }
    }
}