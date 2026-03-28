using System;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Silmoon.AI.OpenAI;
using Silmoon.Extensions;
using Silmoon.Models;

namespace Silmoon.AI.Network;

public class SseHttpClient : HttpClient
{
    JsonSerializerSettings serializerSettings = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };
    public async Task<StateSet<bool, Response>> CompletionsAsync(string url, Request request)
    {
        request.Stream = false;
        List<Chunk> chunks = [];
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        var jsonString = request.ToJsonString();

        // Console.WriteLine($"Request JSON: {jsonString}");

        httpRequest.Content = new StringContent(jsonString, Encoding.UTF8, "application/json");
        var response = await SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            Response responseData = JsonConvert.DeserializeObject<Response>(json, serializerSettings);
            return true.ToStateSet(responseData);
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Error response: {errorContent}");
            return false.ToStateSet<Response>(null, $"HTTP error: {response.StatusCode}, Content: {errorContent}");
        }
    }
    public async IAsyncEnumerable<Chunk> CompletionsStreamAsync(string url, Request request)
    {
        request.Stream = true;
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        var jsonString = request.ToJsonString(serializerSettings);

        Console.WriteLine($"Request JSON: {jsonString}");

        httpRequest.Content = new StringContent(jsonString, Encoding.UTF8, "application/json");
        var response = await SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                // Console.WriteLine($"{line}");
                if (line.StartsWith("data:"))
                {
                    var json = line[5..].Trim();
                    if (json == "[DONE]") break;
                    Chunk chunkData = JsonConvert.DeserializeObject<Chunk>(json, serializerSettings);
                    yield return chunkData;
                }
            }
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Error response: {errorContent}");
            yield break;
        }
    }
}
