using Azure.Identity;
using Azure.Storage.Blobs;
using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Text;

namespace AITransFunc
{
    public class PreProcess
    {
        private readonly ILogger<PreProcess> _logger;

        public PreProcess(ILogger<PreProcess> logger)
        {
            _logger = logger;
        }

        [Function("PreProcess")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
        {
            // Two URLs are sent to this function via the headers source_url and preprocess_url. 
            // Read the two urls into variables.
            string source_url = req.Headers["source_url"];
            string preprocess_url = req.Headers["preprocess_url"];

            // Load the blob content into a string variable
            string content = await ReadBlob(source_url);

            string regex = "(?<=<\\?Transl start [0-9]*\\?>)[\\w\\s\\d,.<>/()=_\"]*(?=<\\?Transl end\\?>)";
            var matches = Regex.Matches(content, regex);

            StringBuilder newContent = new StringBuilder();
            int tagCounter = 1;
            foreach (Match match in matches)
            {
                string uniqueTag = $"<Transl_{tagCounter}>";
                content = content.Replace(match.Value, uniqueTag);
                newContent.AppendLine(match.Value);
                tagCounter++;
            }

            // Save the new content to the preprocess_url blob
            await SaveBlob(preprocess_url, newContent.ToString());
            await SaveBlob(preprocess_url.Replace(".txt", ".xml"), content.ToString());

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

        private async Task SaveBlob(string url, string content)
        {
            // Use system managed identity when deployed in Azure
            BlobClient blobClient = new BlobClient(new Uri(url), new DefaultAzureCredential());

            //Catch RequestFailedException
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
                {
                    await blobClient.UploadAsync(stream, overwrite: true);
                }
            }
            catch (RequestFailedException e)
            {
                _logger.LogError($"Error: {e.Message}");
            }
        }
    }
}
