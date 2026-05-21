namespace VFridge.Api.Services;

public interface IAiChatService
{
    /// <summary>
    /// Generates a single assistant reply given a conversation history and a fridge-inventory context.
    /// </summary>
    /// <returns>The assistant's reply text or null if the model returned nothing.</returns>
    Task<string?> GenerateReplyAsync(
        IReadOnlyList<(string Role, string Content)> history,
        string fridgeInventory,
        string userPrompt,
        CancellationToken ct);
}
