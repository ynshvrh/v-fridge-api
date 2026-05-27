using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VFridge.Api.Configuration;
using VFridge.Api.Services;

namespace VFridge.Api.Tests.Unit;

public class OpenRouterMaxTokensTests
{
    [Fact]
    public async Task MealPlanner_Sends_MaxTokens_From_Options()
    {
        var capture = new RequestCapturingHandler(CannedMealPlanResponse());
        var options = MakeOptions(maxTokens: 1234);
        var service = new OpenRouterMealPlannerService(
            new HttpClient(capture),
            options,
            NullLogger<OpenRouterMealPlannerService>.Instance);

        await service.GenerateAsync(Array.Empty<MealPlanInventoryItem>(), CancellationToken.None);

        var body = capture.LastRequestBody.Should().NotBeNull().And.Subject!;
        using var doc = JsonDocument.Parse(body!);
        doc.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(1234);
    }

    [Fact]
    public async Task Chat_Sends_MaxTokens_From_Options()
    {
        var capture = new RequestCapturingHandler(CannedChatResponse());
        var options = MakeOptions(maxTokens: 777);
        var service = new OpenRouterChatService(
            new HttpClient(capture),
            options,
            NullLogger<OpenRouterChatService>.Instance);

        await service.GenerateReplyAsync(
            Array.Empty<(string Role, string Content)>(),
            "empty fridge",
            "what's for dinner?",
            "any",
            CancellationToken.None);

        var body = capture.LastRequestBody.Should().NotBeNull().And.Subject!;
        using var doc = JsonDocument.Parse(body!);
        doc.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(777);
    }

    [Fact]
    public void Default_MaxTokens_Is_Sane()
    {
        // Anchors the chosen ceiling — small enough to survive on free OpenRouter credit,
        // large enough for a meal plan or chat reply. Bumping this is a deliberate decision.
        new OpenRouterOptions().MaxTokens.Should().Be(2048);
    }

    private static IOptions<OpenRouterOptions> MakeOptions(int maxTokens) =>
        Options.Create(new OpenRouterOptions
        {
            ApiKey = "test-key",
            Model = "test-model",
            MaxTokens = maxTokens
        });

    private static HttpResponseMessage CannedMealPlanResponse() =>
        BuildResponse("{\"meals\":[],\"gapItems\":[]}");

    private static HttpResponseMessage CannedChatResponse() =>
        BuildResponse("hello");

    private static HttpResponseMessage BuildResponse(string assistantContent)
    {
        var payload = new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content = assistantContent } }
            }
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
    }

    private sealed class RequestCapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
