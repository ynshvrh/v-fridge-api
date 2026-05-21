namespace VFridge.Api.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string From { get; set; } = "";
    public string DisplayName { get; set; } = "V-Fridge";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool UseStartTls { get; set; } = true;
}
