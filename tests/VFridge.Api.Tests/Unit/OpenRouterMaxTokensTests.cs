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

        await service.GenerateAsync(Array.Empty<MealPlanInventoryItem>(), "any", "en", null, null, null, CancellationToken.None);

        var body = capture.LastRequestBody.Should().NotBeNull().And.Subject!;
        using var doc = JsonDocument.Parse(body!);
        doc.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(1234);
    }

    [Fact]
    public async Task VChefChat_GeneratesStructuredJsonResponse_WhenVChefReturnsRecipe()
    {
        var mockVChef = new FakeVChefClient(new Contracts.VChefRecipeResponse(
            Title: "Омлет з сиром",
            Description: "Смачний та швидкий сніданок",
            PrepTimeMins: 5,
            CookTimeMins: 10,
            Servings: 2,
            Calories: 350,
            ProteinGrams: 22,
            FatGrams: 18,
            CarbsGrams: 4,
            Ingredients: [
                new Contracts.VChefIngredient("Яйця", 2, "шт", true),
                new Contracts.VChefIngredient("Сир", 50, "г", false)
            ],
            Steps: ["Збити яйця", "Посмажити на пательні"],
            GeneratedAt: DateTime.UtcNow));

        var service = new VChefAiChatService(mockVChef, NullLogger<VChefAiChatService>.Instance);

        var reply = await service.GenerateReplyAsync(
            Array.Empty<(string Role, string Content)>(),
            "Яйця [dairy] (10 шт)",
            "що приготувати на сніданок?",
            "ukrainian",
            "uk",
            null,
            CancellationToken.None);

        reply.Should().NotBeNull();
        using var doc = JsonDocument.Parse(reply!);
        doc.RootElement.GetProperty("recipe").GetProperty("name").GetString().Should().Be("Омлет з сиром");
        doc.RootElement.GetProperty("suggestedShoppingItems").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task VChefChat_ReturnsNull_WhenVChefReturnsNull()
    {
        var mockVChef = new FakeVChefClient(null);
        var service = new VChefAiChatService(mockVChef, NullLogger<VChefAiChatService>.Instance);

        var reply = await service.GenerateReplyAsync(
            Array.Empty<(string Role, string Content)>(),
            "empty fridge",
            "hi",
            "any",
            "en",
            null,
            CancellationToken.None);

        reply.Should().BeNull();
    }

    private sealed class FakeVChefClient(Contracts.VChefRecipeResponse? response) : IVChefClient
    {
        public Task<Contracts.VChefRecipeResponse?> GenerateRecipeAsync(Contracts.VChefGenerateRecipeRequest request, CancellationToken ct = default)
            => Task.FromResult(response);

        public Task PingHealthAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task MealPlanner_FailsOverToNextModel_WhenFirstReturnsInvalidJson()
    {
        // A weak model botching the strict JSON schema is exactly what failover must route around.
        var handler = new SequenceHandler(
            CannedChatResponse("not json at all"),
            CannedMealPlanResponse());
        var options = Options.Create(new OpenRouterOptions { ApiKey = "k", Models = ["weak:free", "good:free"], MaxTokens = 2048 });
        var service = new OpenRouterMealPlannerService(new HttpClient(handler), options, NullLogger<OpenRouterMealPlannerService>.Instance);

        var plan = await service.GenerateAsync(Array.Empty<MealPlanInventoryItem>(), "any", "en", null, null, null, CancellationToken.None);

        plan.Should().NotBeNull();
        handler.Requests.Should().HaveCount(2);
        ModelOf(handler.Requests[1]).Should().Be("good:free");
    }

    [Fact]
    public void ResolvedModels_PrefersListThenFallsBackToSingle()
    {
        new OpenRouterOptions { Model = "solo", Models = [] }.ResolvedModels().Should().Equal("solo");
        new OpenRouterOptions { Model = "solo", Models = ["a", "b"] }.ResolvedModels().Should().Equal("a", "b");
        new OpenRouterOptions { Model = "solo", Models = ["", "  "] }.ResolvedModels().Should().Equal("solo");
    }

    private static string? ModelOf(string requestBody)
    {
        using var doc = JsonDocument.Parse(requestBody);
        return doc.RootElement.GetProperty("model").GetString();
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

    private static HttpResponseMessage CannedChatResponse(string content = "hello") =>
        BuildResponse(content);

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

    /// <summary>Returns queued responses in order, one per call, capturing each request body.</summary>
    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _i;
        public List<string> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return responses[_i++];
        }
    }
}
