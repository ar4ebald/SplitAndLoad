using CommandLine;

namespace SplitAndLoad
{
    [Verb("auth", HelpText = "Authenticate using VK login and password")]
    class AuthenticationOptions
    {
        [Value(0, MetaName = "user", Required = true, HelpText = "VK mobile number or e-mail")]
        public string UserName { get; set; }

        [Value(1, MetaName = "password", Required = true, HelpText = "VK password")]
        public string Password { get; set; }
    }
}