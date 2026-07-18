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
    private static readonly List<string> DayList = new()
    {
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    };

    // Light plan: names + ingredients only, NO description/steps. Keeping the response small
    // is what lets the whole 7-day plan fit inside the free-tier token budget; the per-meal
    // recipe (description + steps) is fetched lazily via GenerateRecipeAsync when the user
    // opens a meal card.
    private const string SystemPrompt =
        "You are V-Fridge's meal planner. Given the user's current inventory, propose exactly 21 weekday " +
        "meals (3 meals per day: breakfast, lunch, and dinner, assigned to Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday). For each meal " +
        "give its name, weekday (day), meal type (mealType: must be one of 'breakfast', 'lunch', 'dinner'), and the list of ingredients. Do NOT include cooking steps or a description. Use " +
        "what is in the fridge wherever possible; only ask for extra ingredients when the meal genuinely " +
        "needs them. Do not combine incompatible ingredients (e.g. do not put bananas into borscht or savory salads). If an item cannot be logically used, do not force it into a recipe; instead, suggest a standard meal and list the missing ingredients in 'gapItems'. " +
        "Respond with strict JSON matching this schema, no prose: " +
        "{\"meals\":[{\"name\":string,\"day\":string,\"mealType\":string,\"ingredients\":[string],\"note\":string?}]," +
        "\"gapItems\":[{\"name\":string,\"quantity\":string?,\"unit\":string?,\"category\":string}]} " +
        DayAndCategoryRule;

    // Recipe-only prompt for the lazy fetch: just the description + steps for one named dish.
    private const string RecipeSystemPrompt =
        "You are V-Fridge's chef. For the single named dish, give a one-sentence description, short " +
        "numbered cooking steps, and an estimate of the nutritional values per serving (calories: integer kCal, " +
        "protein: decimal grams, fat: decimal grams, carbs: decimal grams). " +
        "Respond with strict JSON matching this schema, no prose: " +
        "{\"description\":string,\"steps\":[string],\"calories\":integer,\"protein\":number,\"fat\":number,\"carbs\":number} " +
        "If asked to write in another language, write the description and steps in that language.";

    private const string RegenerateDaySystemPrompt =
        "You are V-Fridge's meal planner. Propose exactly 3 meals (breakfast, lunch, dinner) for the requested weekday based on the " +
        "user's current inventory. For each meal, " +
        "give its name, weekday (day), meal type (mealType: must be one of 'breakfast', 'lunch', 'dinner'), and the list of ingredients. Do NOT include cooking steps or a description. " +
        "Do not combine incompatible ingredients (e.g. do not put bananas into borscht or savory salads). If an item cannot be logically used, do not force it into a recipe. " +
        "Respond with strict JSON matching this schema, no prose: " +
        "{\"meals\":[{\"name\":string,\"day\":string,\"mealType\":string,\"ingredients\":[string],\"note\":string?}]} " +
        DayAndCategoryRule;

    private const string RegenerateMealSystemPrompt =
        "You are V-Fridge's meal planner. Propose exactly 1 meal for the requested weekday and meal type (mealType: must be one of 'breakfast', 'lunch', 'dinner') based on the " +
        "user's current inventory. Give its name, weekday (day), meal type (mealType: must be one of 'breakfast', 'lunch', 'dinner'), and the list of ingredients. Do NOT include cooking steps or a description. " +
        "Do not combine incompatible ingredients. If an item cannot be logically used, do not force it into a recipe. " +
        "Respond with strict JSON matching this schema, no prose: " +
        "{\"name\":string,\"day\":string,\"mealType\":string,\"ingredients\":[string],\"note\":string?} " +
        DayAndCategoryRule;

    // Shared trailing rule: machine codes stay English no matter the requested language.
    private const string DayAndCategoryRule =
        "The \"day\" value must always be one of the English weekday names Monday, Tuesday, Wednesday, " +
        "Thursday, Friday, Saturday, Sunday. The \"mealType\" value must always be one of these English codes: breakfast, lunch, dinner. " +
        "The \"category\" must always be one of these English codes: dairy, meat-fish, " +
        "vegetables, fruits, bakery, pantry, snacks, drinks, alcohol, sauces, frozen, canned-prepared, other. " +
        "Never translate \"day\", \"mealType\", or \"category\" — they are machine codes. If asked to write in another " +
        "language, translate only \"name\", \"description\", \"note\" and the \"ingredients\"/\"steps\" strings.";

    private readonly OpenRouterOptions _opts = options.Value;

    public async Task<MealPlanResponse?> GenerateAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        string? dietaryProfile,
        string? currentDay,
        IReadOnlyList<MealPlanMeal>? existingMeals,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            logger.LogWarning("OpenRouter ApiKey is not configured; cannot plan meals");
            return null;
        }

        var dayIndex = -1;
        if (!string.IsNullOrWhiteSpace(currentDay))
        {
            dayIndex = DayList.FindIndex(d => d.Equals(currentDay.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        string activePrompt;
        var keptMeals = new List<MealPlanMeal>();

        if (dayIndex >= 0)
        {
            if (existingMeals != null && existingMeals.Count > 0)
            {
                keptMeals = existingMeals
                    .Where(m => !m.Day.Equals(currentDay, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var existingMealsSummary = keptMeals.Count > 0
                ? string.Join(", ", keptMeals.Select(m => $"{m.Name} ({m.Day})"))
                : "None";

            activePrompt =
                $"You are V-Fridge's meal planner. Given the user's current inventory, propose exactly 3 weekday " +
                $"meals (breakfast, lunch, and dinner, assigned to {currentDay}). For each meal " +
                "give its name, weekday (day), meal type (mealType: must be one of 'breakfast', 'lunch', 'dinner'), and the list of ingredients. Do NOT include cooking steps or a description. Use " +
                "what is in the fridge wherever possible; only ask for extra ingredients when the meal genuinely " +
                "needs them. Do not combine incompatible ingredients. If an item cannot be logically used, do not force it into a recipe; instead, suggest a standard meal and list the missing ingredients in 'gapItems'. " +
                $"We already have meals planned for other days: {existingMealsSummary}. Avoid repeating these dishes if possible. " +
                "Respond with strict JSON matching this schema, no prose: " +
                "{\"meals\":[{\"name\":string,\"day\":string,\"mealType\":string,\"ingredients\":[string],\"note\":string?}]," +
                "\"gapItems\":[{\"name\":string,\"quantity\":string?,\"unit\":string?,\"category\":string}]} " +
                DayAndCategoryRule;
        }
        else
        {
            activePrompt = SystemPrompt;
        }

        var messages = BuildMessages(activePrompt, cuisinePreference, language, dietaryProfile, InventoryText(inventory));

        var root = await SendAndParseAsync(messages, "meal-plan", ct);
        if (root is null) return null;

        var parsedMeals = root.Value.TryGetProperty("meals", out var mealsEl) && mealsEl.ValueKind == JsonValueKind.Array
            ? mealsEl.EnumerateArray().Select(ParseMeal).Where(m => m is not null).Select(m => m!).ToList()
            : new List<MealPlanMeal>();

        var newMeals = parsedMeals;
        if (dayIndex >= 0)
        {
            newMeals = parsedMeals.Where(m => m.Day.Equals(currentDay, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var meals = keptMeals.Concat(newMeals)
            .OrderBy(m => DayList.FindIndex(d => d.Equals(m.Day, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var gapItems = root.Value.TryGetProperty("gapItems", out var gapsEl) && gapsEl.ValueKind == JsonValueKind.Array
            ? gapsEl.EnumerateArray().Select(ParseGap).Where(g => g is not null).Select(g => g!).ToList()
            : new List<MealPlanGapItem>();

        return new MealPlanResponse(meals, gapItems, DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<MealPlanMeal>?> RegenerateDayAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        string day,
        IReadOnlyList<string> avoidMealNames,
        string? dietaryProfile,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            logger.LogWarning("OpenRouter ApiKey is not configured; cannot regenerate meals");
            return null;
        }

        var avoid = avoidMealNames.Count > 0
            ? " Do not repeat any of these existing dishes: " + string.Join(", ", avoidMealNames) + "."
            : string.Empty;
        var userText = $"Propose three meals (breakfast, lunch, dinner) for {day}.{avoid}\n\n{InventoryText(inventory)}";

        var messages = BuildMessages(RegenerateDaySystemPrompt, cuisinePreference, language, dietaryProfile, userText);

        var root = await SendAndParseAsync(messages, "regenerate-day", ct);
        if (root is null) return null;

        var meals = root.Value.TryGetProperty("meals", out var mealsEl) && mealsEl.ValueKind == JsonValueKind.Array
            ? mealsEl.EnumerateArray().Select(ParseMeal).Where(m => m is not null).Select(m => m!).ToList()
            : null;
        if (meals is null || meals.Count == 0) return null;

        // Pin the day to the requested code — the model occasionally drifts or localises it.
        return meals.Select(m => m with { Day = day }).ToList();
    }

    public async Task<MealPlanMeal?> RegenerateMealAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        string day,
        string mealType,
        IReadOnlyList<string> avoidMealNames,
        string? dietaryProfile,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            logger.LogWarning("OpenRouter ApiKey is not configured; cannot regenerate single meal");
            return null;
        }

        var avoid = avoidMealNames.Count > 0
            ? " Do not repeat any of these existing dishes: " + string.Join(", ", avoidMealNames) + "."
            : string.Empty;
        var userText = $"Propose one {mealType} meal for {day}.{avoid}\n\n{InventoryText(inventory)}";

        var messages = BuildMessages(RegenerateMealSystemPrompt, cuisinePreference, language, dietaryProfile, userText);

        var root = await SendAndParseAsync(messages, "regenerate-meal", ct);
        if (root is null) return null;

        var meal = ParseMeal(root.Value);
        if (meal is null) return null;

        // Pin the day and mealType to the requested codes.
        return meal with { Day = day, MealType = mealType };
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

        var calories = root.Value.TryGetProperty("calories", out var cal) && cal.ValueKind == JsonValueKind.Number
            ? cal.GetInt32()
            : 0;
        var protein = root.Value.TryGetProperty("protein", out var prot) && (prot.ValueKind == JsonValueKind.Number || prot.ValueKind == JsonValueKind.String)
            ? (prot.ValueKind == JsonValueKind.Number ? prot.GetDecimal() : decimal.TryParse(prot.GetString(), out var pVal) ? pVal : 0m)
            : 0m;
        var fat = root.Value.TryGetProperty("fat", out var fVal) && (fVal.ValueKind == JsonValueKind.Number || fVal.ValueKind == JsonValueKind.String)
            ? (fVal.ValueKind == JsonValueKind.Number ? fVal.GetDecimal() : decimal.TryParse(fVal.GetString(), out var fNum) ? fNum : 0m)
            : 0m;
        var carbs = root.Value.TryGetProperty("carbs", out var cb) && (cb.ValueKind == JsonValueKind.Number || cb.ValueKind == JsonValueKind.String)
            ? (cb.ValueKind == JsonValueKind.Number ? cb.GetDecimal() : decimal.TryParse(cb.GetString(), out var cVal) ? cVal : 0m)
            : 0m;

        return new MealRecipe(description, steps, calories, protein, fat, carbs);
    }

    private static string InventoryText(IReadOnlyList<MealPlanInventoryItem> inventory) =>
        inventory.Count == 0
            ? "The fridge is empty."
            : "Current inventory:\n" + string.Join("\n",
                inventory.Select(i => $"- {i.Name} [{ProductCategories.Label(i.Category)}] ({i.Quantity} {i.Unit})"));

    private static List<ChatMessage> BuildMessages(string systemPrompt, string cuisinePreference, string language, string? dietaryProfile, string userText)
    {
        var messages = new List<ChatMessage> { new("system", systemPrompt) };

        var culture = AiPrompts.CultureContextFor(SupportedCuisines.Normalize(cuisinePreference));
        if (culture is not null) messages.Add(new ChatMessage("system", culture));

        var languageInstruction = AiPrompts.LanguageInstructionFor(SupportedLanguages.Normalize(language));
        if (languageInstruction is not null) messages.Add(new ChatMessage("system", languageInstruction));

        if (!string.IsNullOrWhiteSpace(dietaryProfile))
        {
            messages.Add(new ChatMessage("system", $"User's dietary restrictions and preferences: {dietaryProfile}"));
        }

        messages.Add(new ChatMessage("user", userText));
        return messages;
    }

    /// <summary>
    /// Sends the request through the model pool, returning the first valid parsed JSON root.
    /// A model that is rate-limited (429), out of credit (402), errors, returns nothing, or
    /// returns invalid JSON falls through to the next — invalid JSON matters here because a
    /// weaker model botching the strict schema is exactly the failure we want to route around.
    /// Returns null only when every model fails.
    /// </summary>
    private async Task<JsonElement?> SendAndParseAsync(List<ChatMessage> messages, string label, CancellationToken ct)
    {
        var models = _opts.ResolvedModels();
        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            var root = await TrySendAndParseAsync(model, messages, label, ct);
            if (root is not null)
            {
                if (i > 0) logger.LogInformation("OpenRouter {Label} served by fallback model {Model} (#{Index})", label, model, i);
                return root;
            }
            logger.LogWarning("OpenRouter {Label} model {Model} unavailable or invalid, trying next", label, model);
        }

        logger.LogError("OpenRouter {Label}: all {Count} models failed", label, models.Count);
        return null;
    }

    /// <summary>One attempt against a single model. Returns the parsed JSON root, or null on any transport/parse failure.</summary>
    private async Task<JsonElement?> TrySendAndParseAsync(string model, List<ChatMessage> messages, string label, CancellationToken ct)
    {
        var body = new ChatCompletionRequest(model, messages, new ResponseFormat("json_object"), _opts.MaxTokens);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
        if (!string.IsNullOrWhiteSpace(_opts.Referer)) request.Headers.TryAddWithoutValidation("HTTP-Referer", _opts.Referer);
        if (!string.IsNullOrWhiteSpace(_opts.Title)) request.Headers.TryAddWithoutValidation("X-Title", _opts.Title);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "OpenRouter {Label} transport error on model {Model}", label, model);
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var raw = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("OpenRouter {Label} {Model} failed: {Status} — {Body}", label, model, (int)response.StatusCode, raw);
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
                logger.LogWarning(ex, "OpenRouter {Label} {Model} returned invalid JSON: {Content}", label, model, content);
                return null;
            }
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
        var mealType = m.TryGetProperty("mealType", out var mt) && mt.ValueKind == JsonValueKind.String ? mt.GetString() : null;

        return new MealPlanMeal(name, day, ingredients, note, description, steps, mealType);
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
