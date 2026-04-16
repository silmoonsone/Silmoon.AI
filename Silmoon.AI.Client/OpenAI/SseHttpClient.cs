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

    public async Task<StateSet<bool, Chunk[]>> CompletionsStreamAsync(string url, Request request, Func<StateSet<bool, Chunk>, Task> callback)
    {
        try
        {
            request.Stream = true;
            var jsonString = request.ToJsonString(serializerSettings);

            //Console.WriteLine($"Request JSON: {jsonString}");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Content = new StringContent(jsonString, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // HttpClient 请求已发起。

            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                List<Chunk> chunks = [];
                string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    //Console.WriteLine($"{line}");
                    if (line.StartsWith("data:"))
                    {
                        try
                        {
                            var json = line[5..].Trim();
                            if (json == "[DONE]")
                            {
                                await callback(true.ToStateSet<Chunk>(null));
                                break;
                            }
                            else
                            {
                                var chunkData = JsonConvert.DeserializeObject<Chunk>(json, serializerSettings);
                                if (chunkData != null && chunkData.Choices is not null)
                                {
                                    chunks.Add(chunkData);
                                    await callback(true.ToStateSet(chunkData));
                                }
                                else await callback(false.ToStateSet<Chunk>(null, json));
                            }
                        }
                        catch (Exception ex)
                        {
                            await callback(false.ToStateSet<Chunk>(null, ex.Message));
                            return false.ToStateSet<Chunk[]>(null, $"Exception during JSON deserialization: {ex.Message}, Line: {line}");
                        }
                    }
                }
                return true.ToStateSet<Chunk[]>([.. chunks]);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                await callback(false.ToStateSet<Chunk>(null, $"Http status code: {response.StatusCode}, Content: {errorContent}"));
                return false.ToStateSet<Chunk[]>(null, $"Http status code: {response.StatusCode}, Content: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            await callback(false.ToStateSet<Chunk>(null, ex.Message));
            return false.ToStateSet<Chunk[]>(null, ex.Message);
        }
    }
}