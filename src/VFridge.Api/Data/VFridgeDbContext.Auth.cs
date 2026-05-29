using Microsoft.EntityFrameworkCore;
using VFridge.Api.Data.Entities;

namespace VFridge.Api.Data;

public partial class VFridgeDbContext
{
    public DbSet<EmailVerification> EmailVerifications => Set<EmailVerification>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<OAuthLogin> OAuthLogins => Set<OAuthLogin>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ShoppingItem> ShoppingItems => Set<ShoppingItem>();
    public DbSet<ConsumptionLog> ConsumptionLogs => Set<ConsumptionLog>();
    public DbSet<Fridge> Fridges => Set<Fridge>();
    public DbSet<FridgeMember> FridgeMembers => Set<FridgeMember>();
    public DbSet<FridgeInvite> FridgeInvites => Set<FridgeInvite>();
    public DbSet<MealPlanRecord> MealPlans => Set<MealPlanRecord>();

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailVerification>(entity =>
        {
            entity.ToTable("email_verifications");
            entity.HasKey(e => e.UserId);

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.VerifiedAt)
                .HasColumnName("verified_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.User).WithOne()
                .HasForeignKey<EmailVerification>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.ToTable("email_verification_tokens");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.TokenHash).HasColumnName("token_hash");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.UsedAt).HasColumnName("used_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.TokenHash).HasDatabaseName("ix_email_verification_tokens_hash");
            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_email_verification_tokens_user");

            entity.HasOne(e => e.User).WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OAuthLogin>(entity =>
        {
            entity.ToTable("oauth_logins");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(20);
            entity.Property(e => e.ProviderUserId).HasColumnName("provider_user_id").HasMaxLength(255);
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.Provider, e.ProviderUserId }).IsUnique();
            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_oauth_logins_user");

            entity.HasOne(e => e.User).WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShoppingItem>(entity =>
        {
            entity.ToTable("shopping_items");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.FridgeId).HasColumnName("fridge_id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.Quantity).HasColumnName("quantity").HasPrecision(10, 2);
            entity.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(20);
            entity.Property(e => e.Category)
                .HasColumnName("category")
                .HasMaxLength(32)
                .HasDefaultValue(VFridge.Api.Contracts.ProductCategories.Other);
            entity.Property(e => e.Checked).HasColumnName("checked");
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_shopping_items_user");
            entity.HasIndex(e => new { e.UserId, e.Checked }).HasDatabaseName("ix_shopping_items_user_checked");
            entity.HasIndex(e => e.FridgeId).HasDatabaseName("ix_shopping_items_fridge");

            entity.HasOne(e => e.User).WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Fridge>(entity =>
        {
            entity.ToTable("fridges");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(80);
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.OwnerId).HasDatabaseName("ix_fridges_owner");

            entity.HasOne(e => e.Owner).WithMany()
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FridgeMember>(entity =>
        {
            entity.ToTable("fridge_members");
            entity.HasKey(e => new { e.FridgeId, e.UserId });

            entity.Property(e => e.FridgeId).HasColumnName("fridge_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Role).HasColumnName("role").HasMaxLength(16);
            entity.Property(e => e.JoinedAt)
                .HasColumnName("joined_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_fridge_members_user");

            entity.HasOne(e => e.Fridge).WithMany(f => f.Members)
                .HasForeignKey(e => e.FridgeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User).WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FridgeInvite>(entity =>
        {
            entity.ToTable("fridge_invites");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FridgeId).HasColumnName("fridge_id");
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(255);
            entity.Property(e => e.TokenHash).HasColumnName("token_hash");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.AcceptedAt).HasColumnName("accepted_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.TokenHash).HasDatabaseName("ix_fridge_invites_hash");
            entity.HasIndex(e => e.FridgeId).HasDatabaseName("ix_fridge_invites_fridge");

            entity.HasOne(e => e.Fridge).WithMany()
                .HasForeignKey(e => e.FridgeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Ignore(e => e.IsClaimable);
        });

        modelBuilder.Entity<ConsumptionLog>(entity =>
        {
            entity.ToTable("consumption_log");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ProductName).HasColumnName("product_name").HasMaxLength(255);
            entity.Property(e => e.Quantity).HasColumnName("quantity").HasPrecision(10, 2);
            entity.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(20);
            entity.Property(e => e.Category)
                .HasColumnName("category")
                .HasMaxLength(32)
                .HasDefaultValue(VFridge.Api.Contracts.ProductCategories.Other);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(16);
            entity.Property(e => e.AgeDays).HasColumnName("age_days");
            entity.Property(e => e.ConsumedAt)
                .HasColumnName("consumed_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.ConsumedAt }).HasDatabaseName("ix_consumption_log_user_consumed_at");
            entity.HasIndex(e => new { e.UserId, e.Status }).HasDatabaseName("ix_consumption_log_user_status");
        });

        modelBuilder.Entity<MealPlanRecord>(entity =>
        {
            entity.ToTable("meal_plans");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FridgeId).HasColumnName("fridge_id");
            entity.Property(e => e.MealsJson).HasColumnName("meals_json");
            entity.Property(e => e.GapItemsJson).HasColumnName("gap_items_json");
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.FridgeId).IsUnique();

            entity.HasOne(e => e.Fridge).WithMany()
                .HasForeignKey(e => e.FridgeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.TokenHash).HasColumnName("token_hash");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_refresh_tokens_user");

            entity.HasOne(e => e.User).WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
