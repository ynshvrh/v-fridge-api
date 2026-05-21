namespace VFridge.Api.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>'smtp' (default — Gmail / Mailgun SMTP) or 'resend' (HTTPS API at resend.com).</summary>
    /// <remarks>
    /// Cloud platforms (Render free tier, Heroku, etc.) often block outbound SMTP. Switching to
    /// 'resend' makes deliveries go over HTTPS and bypasses the block.
    /// </remarks>
    public string Provider { get; set; } = "smtp";

    public string From { get; set; } = "";
    public string DisplayName { get; set; } = "V-Fridge";

    // --- SMTP-only ---
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool UseStartTls { get; set; } = true;

    // --- Resend-only ---
    public string ApiKey { get; set; } = "";
}
