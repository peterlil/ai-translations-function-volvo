using Azure;
using Azure.AI.Translation.Text;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AITransFunc
{
    public class StartTranslation
    {
        private readonly ILogger<StartTranslation> _logger;
        private readonly IHostEnvironment _env; // Inject IHostEnvironment
        private readonly string _userAssignedClientId = "e13d6cb9-462e-4fa1-a60d-ee4710295e4b";

        public StartTranslation(ILogger<StartTranslation> logger, IHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        [Function("StartTranslation")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
        {
            // Two URLs are sent to this function via the headers translated_url and preprocess_url. 
            // Read the two urls into variables.
            string translated_url = req.Headers["translated_url"];
            string preprocess_url = req.Headers["preprocess_url"];

            // Read the header as_html and if it is not present, set it to false
            bool as_html = req.Headers["as_html"] == "true";

            // Read the header tag_replacement to a string variable and if it's not present or an emppty string, set the variable to empty.
            string tag_replacement = req.Headers["tag_replacement"];
            if (string.IsNullOrEmpty(tag_replacement))
            {
                tag_replacement = string.Empty;
            }

            // Sanity checks to ensure that the URLs are valid
            if (string.IsNullOrEmpty(translated_url) || string.IsNullOrEmpty(preprocess_url))
            {
                return new BadRequestObjectResult("translated_url and preprocess_url are required.");
            }

            _logger.LogDebug($"translated_url: {translated_url}");
            _logger.LogDebug($"preprocess_url: {preprocess_url}");
            _logger.LogDebug($"as_html: {as_html}");
            _logger.LogDebug($"tag_replacement: {tag_replacement}");

            // Load the blob content into a string variable
            string content = await ReadBlob(preprocess_url);
            _logger.LogTrace($"Content from preprocessed_url: {content}");

            List<string> replacedTags = new List<string>();

            if (!string.IsNullOrEmpty(tag_replacement))
            {
                _logger.LogDebug($"Tags will be replaced with {tag_replacement}.");
                content = ReplaceXmlTags(content, tag_replacement, replacedTags);
                _logger.LogTrace($"Content after replacing tags: {content}");
            }

            TextTranslationClient client;
            _logger.LogDebug("Creating TextTranslationClient");

            if (_env.IsDevelopment())
            {
                client = new TextTranslationClient(new DefaultAzureCredential());
            }
            else
            {
                // Use user-assigned managed identity when deployed in Azure
                var credentialOptions = new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = _userAssignedClientId
                };
                client = new TextTranslationClient(new DefaultAzureCredential(credentialOptions));
            }
            
            // Translate the content from English to Swedish
            var translationResult = await client.TranslateAsync(
                new List<string> { "sv" },
                new List<string> { content },
                "en",
                textType: as_html ? TextType.Html : TextType.Plain
            );
            string translatedContent = translationResult.Value[0].Translations[0].Text;
            //string translatedContent = "This is a test translation";
            _logger.LogTrace($"Translated content: {translatedContent}");

            if (!string.IsNullOrEmpty(tag_replacement))
            {
                translatedContent = RestoreXmlTags(translatedContent, tag_replacement, replacedTags);
                _logger.LogTrace($"Translated content after restoring tags: {translatedContent}");
            }

            // AI translation sometime adds extra tags, so we need to clean them up
            translatedContent = CleanupXmlTags(translatedContent, tag_replacement);

            // Store the translated content into another blob
            await StoreBlob(translated_url, translatedContent);
            _logger.LogInformation("Translation done!");
            return new OkObjectResult("Translation done!");
        }
        


        private async Task<string> ReadBlob(string url)
        {
            string content = string.Empty;

            BlobClient blobClient;

            if (_env.IsDevelopment())
            {
                blobClient = new BlobClient(new Uri(url), new DefaultAzureCredential());
            }
            else
            {
                // Use user-assigned managed identity when deployed in Azure
                var credentialOptions = new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = _userAssignedClientId
                };
                blobClient = new BlobClient(new Uri(url), new DefaultAzureCredential(credentialOptions));
            }

            //Catch RequestFailedException
            try
            {
                var downloadResponse = await blobClient.DownloadAsync();
                using (var streamReader = new StreamReader(downloadResponse.Value.Content))
                {
                    content = await streamReader.ReadToEndAsync();
                }
            }
            catch (RequestFailedException e)
            {
                _logger.LogError($"Error: {e.Message}");
            }

            return content;

        }

        private async Task StoreBlob(string url, string content)
        {
            BlobClient blobClient;

            if (_env.IsDevelopment())
            {
                blobClient = new BlobClient(new Uri(url), new DefaultAzureCredential());
            }
            else
            {
                // Use user-assigned managed identity when deployed in Azure
                var credentialOptions = new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = _userAssignedClientId
                };
                blobClient = new BlobClient(new Uri(url), new DefaultAzureCredential(credentialOptions));
            }

            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)))
            {
                await blobClient.UploadAsync(stream, overwrite: true);
            }
        }

        private string ReplaceXmlTags(string content, string replaceValue, List<string> replacedTags)
        {
            string pattern = "<[^>]+>";
            return Regex.Replace(content, pattern, match =>
            {
                replacedTags.Add(match.Value);
                return replaceValue;
            });
        }

        private string RestoreXmlTags(string content, string searchString, List<string> replacedTags)
        {
            foreach (var tag in replacedTags)
            {
                content = content.ReplaceFirst(searchString, tag);
            }
            return content;
        }

        private string CleanupXmlTags(string content, string replaceValue)
        {
            string pattern = "<[^>]+>";
            return Regex.Replace(content, pattern, "");
        }
    }

    public static class StringExtensions
    {
        public static string ReplaceFirst(this string text, string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0)
            {
                return text;
            }
            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }
    }
}
