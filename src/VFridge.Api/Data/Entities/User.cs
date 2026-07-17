using System;
using System.Collections.Generic;

namespace VFridge.Api.Data.Entities;

public partial class User
{
    public int Id { get; set; }

    public string Username { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string Password { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public string PreferredLanguage { get; set; } = "en";

    public string CuisinePreference { get; set; } = "any";

    public virtual ICollection<Chat> Chats { get; set; } = new List<Chat>();

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();

    public string? DietaryProfile { get; set; }
}
