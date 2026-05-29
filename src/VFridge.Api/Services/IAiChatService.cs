namespace VFridge.Api.Services;

public interface IAiChatService
{
    /// <summary>
    /// Generates a single assistant reply given a conversation history, a fridge-inventory context,
    /// the user's cuisine preference (culinary steering) and preferred language (the reply is
    /// written in that language; English is the default).
    /// </summary>
    /// <returns>The assistant's reply text or null if the model returned nothing.</returns>
    Task<string?> GenerateReplyAsync(
        IReadOnlyList<(string Role, string Content)> history,
        string fridgeInventory,
        string userPrompt,
        string cuisinePreference,
        string language,
        CancellationToken ct);
}
