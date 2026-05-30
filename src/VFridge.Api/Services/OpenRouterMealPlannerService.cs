using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using VFridge.Api.Configuration;
using VFridge.Api.Contracts;

namespace VFridge.Api.Services;

public sealed class OpenRouterMealPlannerService(
    HttpClient http,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterMealPlannerService> logger) : IMealPlannerService
{
    // Light plan: names + ingredients only, NO description/steps. Keeping the response small
    // is what lets the whole 5-meal plan fit inside the free-tier token budget; the per-meal
    // recipe (description + steps) is fetched lazily via GenerateRecipeAsync when the user
    // opens a meal card.
    private const string SystemPrompt =
        "You are V-Fridge's meal planner. Given the user's current inventory, propose exactly 5 distinct " +
        "weekday meals (assign each to one of Monday, Tuesday, Wednesday, Thursday, Friday). For each meal " +
        "give its name and the list of ingredients. Do NOT include cooking steps or a description. Use " +
        "what is in the fridge wherever possible; only ask for extra ingredients when the meal genuinely " +
        "needs them. " +
        "Respond with strict JSON matching this schema, no prose: " +
        "{\"meals\":[{\"name\":string,\"day\":string,\"ingredients\":[string],\"note\":string?}]," +
        "\"gapItems\":[{\"name\":string,\"quantity\":string?,\"unit\":string?,\"category\":string}]} " +
        DayAndCategoryRule;

    // Recipe-only prompt for the lazy fetch: just the description + steps for one named dish.
    private const string RecipeSystemPrompt =
        "You are V-Fridge's chef. For the single named dish, give a one-sentence description and short " +
        "numbered cooking steps. " +
        "Respond with strict JSON matching this schema, no prose: " +
        "{\"description\":string,\"steps\":[string]} " +
        "If asked to write in another language, write the description and steps in that language.";

    private const string SingleMealSystemPrompt =
        "You are V-Fridge's meal planner. Propose exactly ONE meal for the requested weekday based on the " +
        "user's current inventory. Give a one-sentence description, the list of ingredients, and short " +
        "numbered cooking steps. " +
        "Respond with strict JSON matching this schema, no prose: " +
        "{\"name\":string,\"day\":string,\"description\":string,\"ingredients\":[string],\"steps\":[string],\"note\":string?} " +
        DayAndCategoryRule;

    // Shared trailing rule: machine codes stay English no matter the requested language.
    private const string DayAndCategoryRule =
        "The \"day\" value must always be one of the English weekday names Monday, Tuesday, Wednesday, " +
        "Thursday, Friday, and \"category\" must always be one of these English codes: dairy, meat-fish, " +
        "vegetables, fruits, bakery, pantry, snacks, drinks, alcohol, sauces, frozen, canned-prepared, other. " +
        "Never translate \"day\" or \"category\" — they are machine codes. If asked to write in another " +
        "language, translate only \"name\", \"description\", \"note\" and the \"ingredients\"/\"steps\" strings.";

    private readonly OpenRouterOptions _opts = options.Value;

    public async Task<MealPlanResponse?> GenerateAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            logger.LogWarning("OpenRouter ApiKey is not configured; cannot plan meals");
            return null;
        }

        var messages = BuildMessages(SystemPrompt, cuisinePreference, language, InventoryText(inventory));

        var root = await SendAndParseAsync(messages, "meal-plan", ct);
        if (root is null) return null;

        var meals = root.Value.TryGetProperty("meals", out var mealsEl) && mealsEl.ValueKind == JsonValueKind.Array
            ? mealsEl.EnumerateArray().Select(ParseMeal).Where(m => m is not null).Select(m => m!).ToList()
            : new List<MealPlanMeal>();

        var gapItems = root.Value.TryGetProperty("gapItems", out var gapsEl) && gapsEl.ValueKind == JsonValueKind.Array
            ? gapsEl.EnumerateArray().Select(ParseGap).Where(g => g is not null).Select(g => g!).ToList()
            : new List<MealPlanGapItem>();

        return new MealPlanResponse(meals, gapItems, DateTime.UtcNow);
    }

    public async Task<MealPlanMeal?> RegenerateDayAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        string day,
        IReadOnlyList<string> avoidMealNames,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            logger.LogWarning("OpenRouter ApiKey is not configured; cannot regenerate a meal");
            return null;
        }

        var avoid = avoidMealNames.Count > 0
            ? " Do not repeat any of these existing dishes: " + string.Join(", ", avoidMealNames) + "."
            : string.Empty;
        var userText = $"Propose one meal for {day}.{avoid}\n\n{InventoryText(inventory)}";

        var messages = BuildMessages(SingleMealSystemPrompt, cuisinePreference, language, userText);

        var root = await SendAndParseAsync(messages, "regenerate-day", ct);
        if (root is null) return null;

        var meal = ParseMeal(root.Value);
        if (meal is null) return null;

        // Pin the day to the requested code — the model occasionally drifts or localises it.
        return meal with { Day = day };
    }

    public async Task<MealRecipe?> GenerateRecipeAsync(
        string mealName,
        IReadOnlyList<string> ingredients,
        string language,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            logger.LogWarning("OpenRouter ApiKey is not configured; cannot fetch a recipe");
            return null;
        }

        var ingredientsText = ingredients.Count > 0
            ? " It uses: " + string.Join(", ", ingredients) + "."
            : string.Empty;
        var userText = $"Dish: {mealName}.{ingredientsText}";

        // No cuisine steering here — the dish is already chosen; we only need its recipe.
        var messages = new List<ChatMessage> { new("system", RecipeSystemPrompt) };
        var languageInstruction = AiPrompts.LanguageInstructionFor(SupportedLanguages.Normalize(language));
        if (languageInstruction is not null) messages.Add(new ChatMessage("system", languageInstruction));
        messages.Add(new ChatMessage("user", userText));

        var root = await SendAndParseAsync(messages, "recipe", ct);
        if (root is null) return null;

        var description = root.Value.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;
        var steps = StringList(root.Value, "steps");
        if (string.IsNullOrWhiteSpace(description) && steps.Count == 0) return null;

        return new MealRecipe(description, steps);
    }

    private static string InventoryText(IReadOnlyList<MealPlanInventoryItem> inventory) =>
        inventory.Count == 0
            ? "The fridge is empty."
            : "Current inventory:\n" + string.Join("\n",
                inventory.Select(i => $"- {i.Name} [{ProductCategories.Label(i.Category)}] ({i.Quantity} {i.Unit})"));

    private static List<ChatMessage> BuildMessages(string systemPrompt, string cuisinePreference, string language, string userText)
    {
        var messages = new List<ChatMessage> { new("system", systemPrompt) };

        var culture = AiPrompts.CultureContextFor(SupportedCuisines.Normalize(cuisinePreference));
        if (culture is not null) messages.Add(new ChatMessage("system", culture));

        var languageInstruction = AiPrompts.LanguageInstructionFor(SupportedLanguages.Normalize(language));
        if (languageInstruction is not null) messages.Add(new ChatMessage("system", languageInstruction));

        messages.Add(new ChatMessage("user", userText));
        return messages;
    }

    /// <summary>Sends the chat request, returns the parsed JSON root, or null on any transport/parse failure.</summary>
    private async Task<JsonElement?> SendAndParseAsync(List<ChatMessage> messages, string label, CancellationToken ct)
    {
        var body = new ChatCompletionRequest(_opts.Model, messages, new ResponseFormat("json_object"), _opts.MaxTokens);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
        if (!string.IsNullOrWhiteSpace(_opts.Referer)) request.Headers.TryAddWithoutValidation("HTTP-Referer", _opts.Referer);
        if (!string.IsNullOrWhiteSpace(_opts.Title)) request.Headers.TryAddWithoutValidation("X-Title", _opts.Title);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("OpenRouter {Label} call failed: {Status} — {Body}", label, (int)response.StatusCode, raw);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: ct);
        var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            // Clone so the JsonElement stays valid after the JsonDocument is disposed.
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "OpenRouter {Label} response was not valid JSON: {Content}", label, content);
            return null;
        }
    }

    private static MealPlanMeal? ParseMeal(JsonElement m)
    {
        if (m.ValueKind != JsonValueKind.Object) return null;
        var name = m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(name)) return null;

        var day = m.TryGetProperty("day", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
        var description = m.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
            ? desc.GetString()
            : null;
        var ingredients = StringList(m, "ingredients");
        var steps = StringList(m, "steps");
        var note = m.TryGetProperty("note", out var nt) && nt.ValueKind == JsonValueKind.String ? nt.GetString() : null;

        return new MealPlanMeal(name, day, ingredients, note, description, steps);
    }

    private static MealPlanGapItem? ParseGap(JsonElement g)
    {
        if (g.ValueKind != JsonValueKind.Object) return null;
        var name = g.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(name)) return null;

        var quantity = g.TryGetProperty("quantity", out var q) && q.ValueKind != JsonValueKind.Null ? q.ToString() : null;
        var unit = g.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        var category = g.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String
            ? (ProductCategories.IsValid(c.GetString() ?? "") ? c.GetString()! : ProductCategories.Other)
            : ProductCategories.Other;

        return new MealPlanGapItem(name, quantity, unit, category);
    }

    private static List<string> StringList(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? "")
                .Where(x => x.Length > 0)
                .ToList()
            : new List<string>();

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ResponseFormat([property: JsonPropertyName("type")] string Type);

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("response_format")] ResponseFormat ResponseFormat,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }
}
