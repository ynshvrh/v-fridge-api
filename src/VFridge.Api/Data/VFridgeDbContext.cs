using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Data.Entities;

namespace VFridge.Api.Data;

public partial class VFridgeDbContext : DbContext
{
    public VFridgeDbContext(DbContextOptions<VFridgeDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Chat> Chats { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<NutritionLog> NutritionLogs { get; set; }

    public virtual DbSet<SavedRecipeRecord> SavedRecipes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("chat_pkey");

            entity.ToTable("chat");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Role)
                .HasMaxLength(20)
                .HasColumnName("role");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.Chats)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("chat_user_id_fkey");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("products_pkey");

            entity.ToTable("products");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.FridgeId).HasColumnName("fridge_id");
            entity.Property(e => e.Quantity)
                .HasPrecision(10, 2)
                .HasColumnName("quantity");
            entity.Property(e => e.Unit)
                .HasMaxLength(20)
                .HasColumnName("unit");
            entity.Property(e => e.Category)
                .HasMaxLength(32)
                .HasDefaultValue(VFridge.Api.Contracts.ProductCategories.Other)
                .HasColumnName("category");

            entity.HasOne(d => d.Owner).WithMany(p => p.Products)
                .HasForeignKey(d => d.OwnerId)
                .HasConstraintName("products_owner_id_fkey");

            entity.HasOne(d => d.Fridge).WithMany()
                .HasForeignKey(d => d.FridgeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.FridgeId).HasDatabaseName("ix_products_fridge");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "users_email_key").IsUnique();
            // Username is a non-unique display name. See Migrations/006_username_display_name.sql.

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .HasColumnName("email");
            entity.Property(e => e.Password).HasColumnName("password");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");
            entity.Property(e => e.PreferredLanguage)
                .HasMaxLength(8)
                .HasDefaultValue("en")
                .HasColumnName("preferred_language");
            entity.Property(e => e.CuisinePreference)
                .HasMaxLength(32)
                .HasDefaultValue("any")
                .HasColumnName("cuisine_preference");
            entity.Property(e => e.DietaryProfile)
                .HasMaxLength(1000)
                .HasColumnName("dietary_profile");
            entity.Property(e => e.Avatar)
                .HasMaxLength(50)
                .HasColumnName("avatar");
            entity.Property(e => e.DailyCaloriesTarget).HasColumnName("daily_calories_target");
            entity.Property(e => e.DailyProteinTarget)
                .HasPrecision(6, 2)
                .HasColumnName("daily_protein_target");
            entity.Property(e => e.DailyFatTarget)
                .HasPrecision(6, 2)
                .HasColumnName("daily_fat_target");
            entity.Property(e => e.DailyCarbsTarget)
                .HasPrecision(6, 2)
                .HasColumnName("daily_carbs_target");
        });

        modelBuilder.Entity<NutritionLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("nutrition_logs_pkey");

            entity.ToTable("nutrition_logs");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Date).HasColumnName("date");
            entity.Property(e => e.MealType)
                .HasMaxLength(32)
                .HasColumnName("meal_type");
            entity.Property(e => e.FoodName)
                .HasMaxLength(255)
                .HasColumnName("food_name");
            entity.Property(e => e.Quantity)
                .HasPrecision(10, 2)
                .HasColumnName("quantity");
            entity.Property(e => e.Unit)
                .HasMaxLength(20)
                .HasColumnName("unit");
            entity.Property(e => e.Calories).HasColumnName("calories");
            entity.Property(e => e.Protein)
                .HasPrecision(6, 2)
                .HasColumnName("protein");
            entity.Property(e => e.Fat)
                .HasPrecision(6, 2)
                .HasColumnName("fat");
            entity.Property(e => e.Carbs)
                .HasPrecision(6, 2)
                .HasColumnName("carbs");
            entity.Property(e => e.LoggedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("logged_at");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("nutrition_logs_user_id_fkey")
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.Date }).HasDatabaseName("ix_nutrition_logs_user_date");
        });

        modelBuilder.Entity<SavedRecipeRecord>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("saved_recipes_pkey");

            entity.ToTable("saved_recipes");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.FridgeId).HasColumnName("fridge_id");
            entity.Property(e => e.Name).HasMaxLength(255).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IngredientsJson).HasColumnName("ingredients_json");
            entity.Property(e => e.StepsJson).HasColumnName("steps_json");
            entity.Property(e => e.Calories).HasColumnName("calories");
            entity.Property(e => e.Protein).HasPrecision(6, 2).HasColumnName("protein");
            entity.Property(e => e.Fat).HasPrecision(6, 2).HasColumnName("fat");
            entity.Property(e => e.Carbs).HasPrecision(6, 2).HasColumnName("carbs");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_saved_recipes_user_id");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
