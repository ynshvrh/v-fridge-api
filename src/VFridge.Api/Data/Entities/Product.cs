using System;
using System.Collections.Generic;

namespace VFridge.Api.Data.Entities;

public partial class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = null!;

    public DateOnly? ExpiryDate { get; set; }

    public int OwnerId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User Owner { get; set; } = null!;
}
