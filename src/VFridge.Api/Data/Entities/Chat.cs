using System;
using System.Collections.Generic;

namespace VFridge.Api.Data.Entities;

public partial class Chat
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Role { get; set; } = null!;

    public string Content { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
