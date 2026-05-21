using System;
using System.Collections.Generic;
using VFridge.Api.Contracts;

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

    public string Category { get; set; } = ProductCategories.Other;

    public virtual User Owner { get; set; } = null!;
}
