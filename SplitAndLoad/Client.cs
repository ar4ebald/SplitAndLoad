using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SplitAndLoad
{
    public class Client
    {
        public delegate KeyValuePair<string, string>[] ArgsExtractionDelegate(object args);

        private const string Version = "5.63";
        private static readonly Uri ApiUri = new Uri("https://api.vk.com/method/");

        private static readonly ConcurrentDictionary<Type, ArgsExtractionDelegate> CachedExtractors =
            new ConcurrentDictionary<Type, ArgsExtractionDelegate>();

        public string AccessToken { get; }
        public long IdentityId { get; }

        public Client(string accessToken, long identityId)
        {
            AccessToken = accessToken;
            IdentityId = identityId;
        }

        private static IEnumerable<char> ConvertPascalToSnakeCase(string text)
        {
            yield return text[0];

            for (int i = 1; i < text.Length; i++)
            {
                if (char.IsUpper(text[i]) && (char.IsLower(text[i - 1]) || char.IsDigit(text[i - 1])) ||
                    char.IsDigit(text[i]) && char.IsLetter(text[i - 1]))
                    yield return '_';

                yield return text[i];
            }
        }

        private static ArgsExtractionDelegate BuildExtractor(Type type)
        {
            var pairCtor = typeof(KeyValuePair<string, string>).GetTypeInfo().GetConstructors().First();

            var arg = Expression.Parameter(typeof(object), "args");
            var local = Expression.Variable(type);
            var localInitializer = Expression.Assign(local, Expression.Convert(arg, type));

            var properties = type.GetRuntimeProperties().ToArray();
            var initializer = Expression.NewArrayInit(
                typeof(KeyValuePair<string, string>),
                properties.Select(property =>
                {
                    var propertyName = new string(ConvertPascalToSnakeCase(property.Name).ToArray()).ToLowerInvariant();
                    Expression key = Expression.Constant(propertyName);
                    Expression value = Expression.Property(local, property);
                    if (property.PropertyType != typeof(string))
                        value = Expression.Call(value, "ToString", null);
                    return Expression.New(pairCtor, key, value);
                }));

            return
                Expression.Lambda<ArgsExtractionDelegate>(
                    Expression.Block(new[] { local }, localInitializer, initializer), arg).Compile();
        }

        public static async Task<JToken> GetAnonAsync<T>(string method, T args = null, string accessToken = null) where T : class
        {
            if (!CachedExtractors.TryGetValue(typeof(T), out var extractor))
                CachedExtractors.GetOrAdd(typeof(T), extractor = BuildExtractor(typeof(T)));

            var relativeUri = $"{method}?v={Version}&access_token={accessToken}";
            var argsContent = new FormUrlEncodedContent(args == null ? null : extractor(args));

            JToken result;
            using (var client = new HttpClient())
            using (var responseMessage = await client.PostAsync(new Uri(ApiUri, relativeUri), argsContent))
            using (var stream = await responseMessage.Content.ReadAsStreamAsync())
            using (var textReader = new StreamReader(stream))
            using (var jsonReader = new JsonTextReader(textReader))
                result = JToken.ReadFrom(jsonReader);

            JToken response;
            if ((response = result["response"]) != null)
                return response;

            return result;
        }

        public static async Task<Client> AuthenticateAsync(string login, string password, int clientId, string scope)
        {
            var cookieContainer = new CookieContainer();
            using (var handler = new HttpClientHandler() { CookieContainer = cookieContainer })
            using (var client = new HttpClient(handler))
            {
                string request = $"https://oauth.vk.com/authorize?client_id={clientId}&redirect_uri=https%3A%2F%2Foauth.vk.com%2Fblank.html&display=mobile&scope={scope}&response_type=token&v={Version}&revoke=1";
                var html = await client.GetStringAsync(request);
                string formHtml = Regex.Match(html, @"<form.*</form>", RegexOptions.Singleline).Value;

                var inputElements = Regex.Matches(formHtml, @"<input\s+.*?name\s*=\s*""(?<name>[^""]*)""(.*?value\s*=\s*""(?<value>[^""]*)"")?");
                var arguments = inputElements.Cast<Match>()
                    .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);

                arguments["email"] = login;
                arguments["pass"] = password;

                var content = new FormUrlEncodedContent(arguments);
                request = Regex.Match(html, @"(?<=action\s*=\s*"")[^""]*(?="")").Value;

                using (var response = await client.PostAsync(request, content))
                    html = await response.Content.ReadAsStringAsync();

                request = Regex.Match(html, @"(?<=action\s*=\s*"")[^""]*(?="")").Value;
                using (var response = await client.PostAsync(request, new FormUrlEncodedContent(new KeyValuePair<string, string>[0])))
                {
                    var result = response.RequestMessage.RequestUri.Fragment.TrimStart('#')
                        .Split('&')
                        .Select(pair => pair.Split('='))
                        .ToDictionary(pair => pair[0], pair => pair[1]);
                    return new Client(result["access_token"], Convert.ToInt64(result["user_id"]));
                }
            }
        }

        public Task<JToken> GetAsync<T>(string method, T args = null) where T : class
        {
            return GetAnonAsync(method, args, AccessToken);
        }

        public async Task<bool> CheckTokenIsValidAsync()
        {
            var response = await GetAsync("users.get", new { UserIds = IdentityId });

            if (response is JObject)
                return false;

            return response?.FirstOrDefault()?.Value<long>("id") == IdentityId;
        }
    }
}
