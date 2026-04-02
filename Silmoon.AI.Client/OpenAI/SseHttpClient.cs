using System;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Silmoon.AI.Client.OpenAI.Models;
using Silmoon.Extensions;
using Silmoon.Models;

namespace Silmoon.AI.Client.OpenAI;

public class SseHttpClient : HttpClient
{
    JsonSerializerSettings serializerSettings = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };
    public async Task<StateSet<bool, Response>> CompletionsAsync(string url, Request request)
    {
        try
        {
            request.Stream = false;
            List<Chunk> chunks = [];
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            var jsonString = request.ToJsonString(serializerSettings);

            //Console.WriteLine($"Request JSON: {jsonString}\r\n");

            httpRequest.Content = new StringContent(jsonString, Encoding.UTF8, "application/json");
            using var response = await SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                //Console.WriteLine(json);
                Response responseData = JsonConvert.DeserializeObject<Response>(json, serializerSettings);
                return true.ToStateSet(responseData);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                //Console.WriteLine($"Error response: {errorContent}");
                return false.ToStateSet<Response>(null, $"HTTP error: {response.StatusCode}, Content: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            return false.ToStateSet<Response>(null, $"Exception during HTTP request: {ex.Message}");
        }
    }
    public async IAsyncEnumerable<StateSet<bool, Chunk>> CompletionsStreamAsync(string url, Request request)
    {
        request.Stream = true;
        var jsonString = request.ToJsonString(serializerSettings);

        //Console.WriteLine($"Request JSON: {jsonString}");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Content = new StringContent(jsonString, Encoding.UTF8, "application/json");
        HttpResponseMessage response = null;
        Exception exception = null;
        try { response = await SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead); }
        catch (Exception ex) { exception = ex; }
        if (exception is not null)
        {
            yield return false.ToStateSet<Chunk>(null, $"Exception during HTTP request: {exception.Message}");
            yield break;
        }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    //Console.WriteLine($"{line}");
                    if (line.StartsWith("data:"))
                    {
                        Chunk chunkData = null;
                        try
                        {
                            var json = line[5..].Trim();
                            if (json == "[DONE]") break;
                            chunkData = JsonConvert.DeserializeObject<Chunk>(json, serializerSettings);
                        }
                        catch (Exception ex) { exception = ex; }

                        if (exception is not null)
                        {
                            yield return false.ToStateSet<Chunk>(null, $"Exception during JSON deserialization: {exception.Message}, Line: {line}");
                            yield break;
                        }
                        else yield return true.ToStateSet(chunkData, line);
                    }
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                //Console.WriteLine($"Error response: {errorContent}");
                yield return false.ToStateSet<Chunk>(null, $"HTTP error: {response.StatusCode}, Content: {errorContent}");
            }
        }
    }
}