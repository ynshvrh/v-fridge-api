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

        await service.GenerateAsync(Array.Empty<MealPlanInventoryItem>(), "any", "en", CancellationToken.None);

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
            "en",
            CancellationToken.None);

        var body = capture.LastRequestBody.Should().NotBeNull().And.Subject!;
        using var doc = JsonDocument.Parse(body!);
        doc.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(777);
    }

    [Fact]
    public async Task MealPlanner_Includes_Cuisine_And_Language_Steering_In_Prompt()
    {
        var capture = new RequestCapturingHandler(CannedMealPlanResponse());
        var service = new OpenRouterMealPlannerService(
            new HttpClient(capture),
            MakeOptions(maxTokens: 2048),
            NullLogger<OpenRouterMealPlannerService>.Instance);

        await service.GenerateAsync(
            Array.Empty<MealPlanInventoryItem>(), "ukrainian", "uk", CancellationToken.None);

        using var doc = JsonDocument.Parse(capture.LastRequestBody!);
        var systemText = string.Join("\n", doc.RootElement.GetProperty("messages").EnumerateArray()
            .Where(m => m.GetProperty("role").GetString() == "system")
            .Select(m => m.GetProperty("content").GetString()));

        systemText.Should().Contain("Ukrainian cuisine", "the cuisine preference must steer the plan");
        systemText.Should().Contain("in Ukrainian", "the plan must be written in the user's language");
        // Machine codes must stay English regardless of the requested language.
        systemText.Should().Contain("meat-fish").And.Contain("Never translate");
    }

    [Fact]
    public async Task MealPlanner_Stays_Neutral_For_Any_Cuisine_And_English()
    {
        var capture = new RequestCapturingHandler(CannedMealPlanResponse());
        var service = new OpenRouterMealPlannerService(
            new HttpClient(capture),
            MakeOptions(maxTokens: 2048),
            NullLogger<OpenRouterMealPlannerService>.Instance);

        await service.GenerateAsync(
            Array.Empty<MealPlanInventoryItem>(), "any", "en", CancellationToken.None);

        using var doc = JsonDocument.Parse(capture.LastRequestBody!);
        var systemText = string.Join("\n", doc.RootElement.GetProperty("messages").EnumerateArray()
            .Where(m => m.GetProperty("role").GetString() == "system")
            .Select(m => m.GetProperty("content").GetString()));

        systemText.Should().NotContain("The user prefers");
        systemText.Should().NotContain("in Ukrainian");
    }

    [Fact]
    public void Default_MaxTokens_Is_Sane()
    {
        // Anchors the chosen ceiling — small enough to survive on free OpenRouter credit,
        // large enough for a meal plan or chat reply. Bumping this is a deliberate decision.
        new OpenRouterOptions().MaxTokens.Should().Be(4096);
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
