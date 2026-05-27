using VFridge.Api.Services;

namespace VFridge.Api.Tests.Integration.Infrastructure;

public sealed class FakeAiChatService : IAiChatService
{
    public string Reply { get; set; } = "FAKE_AI_REPLY";

    public int CallCount { get; private set; }

    public string? LastCuisinePreference { get; private set; }

    public Task<string?> GenerateReplyAsync(
        IReadOnlyList<(string Role, string Content)> history,
        string fridgeInventory,
        string userPrompt,
        string cuisinePreference,
        CancellationToken ct)
    {
        CallCount++;
        LastCuisinePreference = cuisinePreference;
        return Task.FromResult<string?>(Reply);
    }
}
