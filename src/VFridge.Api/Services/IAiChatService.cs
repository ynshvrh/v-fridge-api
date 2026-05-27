namespace VFridge.Api.Services;

public interface IAiChatService
{
    /// <summary>
    /// Generates a single assistant reply given a conversation history, a fridge-inventory context,
    /// and the user's preferred language code (used for cultural / regional steering of the reply —
    /// the response text itself stays English per the project's language policy).
    /// </summary>
    /// <returns>The assistant's reply text or null if the model returned nothing.</returns>
    Task<string?> GenerateReplyAsync(
        IReadOnlyList<(string Role, string Content)> history,
        string fridgeInventory,
        string userPrompt,
        string preferredLanguage,
        CancellationToken ct);
}
