using Azure;
using Azure.AI.Translation.Text;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;

namespace AITransFunc
{
    public class StartTranslation
    {
        private readonly ILogger<StartTranslation> _logger;

        public StartTranslation(ILogger<StartTranslation> logger)
        {
            _logger = logger;
        }

        [Function("StartTranslation")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
        {
            // Two URLs are sent to this function via the headers translated_url and preprocess_url. 
            // Read the two urls into variables.
            string translated_url = req.Headers["translated_url"];
            string preprocess_url = req.Headers["preprocess_url"];

            // Sanity checks here to ensure that the URLs are valid
            if (string.IsNullOrEmpty(translated_url) || string.IsNullOrEmpty(preprocess_url))
            {
                return new BadRequestObjectResult("translated_url and preprocess_url are required.");
            }

            _logger.LogInformation($"translated_url: {translated_url}");
            _logger.LogInformation($"preprocess_url: {preprocess_url}");

            // Load the blob content into a string variable
            string content = await ReadBlob(preprocess_url);

            TextTranslationClient client;
            // Use system managed identity when deployed in Azure
            client = new TextTranslationClient(new DefaultAzureCredential());

            // Translate the content from English to Swedish
            var translationResult = await client.TranslateAsync("sv", content, "en");
            string translatedContent = translationResult.Value[0].Translations[0].Text;

            // Store the translated content into another blob
            await StoreBlob(translated_url, translatedContent);

            _logger.LogInformation("C# HTTP trigger function processed a request.");
            return new OkObjectResult("Welcome to Azure Functions!");
        }

        private async Task<string> ReadBlob(string url)
        {
            string content = string.Empty;

            // Use system managed identity when deployed in Azure
            BlobClient blobClient = new BlobClient(new Uri(url), new DefaultAzureCredential());

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

            blobClient = new BlobClient(new Uri(url), new DefaultAzureCredential());

            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)))
            {
                await blobClient.UploadAsync(stream, overwrite: true);
            }
        }
    }
}
