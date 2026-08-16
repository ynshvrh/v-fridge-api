using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VFridge.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumption_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    fridge_id = table.Column<int>(type: "integer", nullable: false),
                    product_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "other"),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    age_days = table.Column<int>(type: "integer", nullable: true),
                    consumed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumption_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    preferred_language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "en"),
                    cuisine_preference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "any"),
                    dietary_profile = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    avatar = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    daily_calories_target = table.Column<int>(type: "integer", nullable: true),
                    daily_protein_target = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    daily_fat_target = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    daily_carbs_target = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("users_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("chat_pkey", x => x.id);
                    table.ForeignKey(
                        name: "chat_user_id_fkey",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_verification_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_verification_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_email_verification_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_verifications",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_verifications", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_email_verifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fridges",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    owner_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fridges", x => x.id);
                    table.ForeignKey(
                        name: "FK_fridges_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "nutrition_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    meal_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    food_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    calories = table.Column<int>(type: "integer", nullable: false),
                    protein = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    fat = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    carbs = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    logged_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("nutrition_logs_pkey", x => x.id);
                    table.ForeignKey(
                        name: "nutrition_logs_user_id_fkey",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oauth_logins",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_logins", x => x.id);
                    table.ForeignKey(
                        name: "FK_oauth_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saved_recipes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    fridge_id = table.Column<int>(type: "integer", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    ingredients_json = table.Column<string>(type: "text", nullable: false),
                    steps_json = table.Column<string>(type: "text", nullable: false),
                    calories = table.Column<int>(type: "integer", nullable: false),
                    protein = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    fat = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    carbs = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("saved_recipes_pkey", x => x.id);
                    table.ForeignKey(
                        name: "FK_saved_recipes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shopping_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    fridge_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "other"),
                    @checked = table.Column<bool>(name: "checked", type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shopping_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_shopping_items_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fridge_invites",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fridge_id = table.Column<int>(type: "integer", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fridge_invites", x => x.id);
                    table.ForeignKey(
                        name: "FK_fridge_invites_fridges_fridge_id",
                        column: x => x.fridge_id,
                        principalTable: "fridges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fridge_members",
                columns: table => new
                {
                    fridge_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fridge_members", x => new { x.fridge_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_fridge_members_fridges_fridge_id",
                        column: x => x.fridge_id,
                        principalTable: "fridges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fridge_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meal_plans",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fridge_id = table.Column<int>(type: "integer", nullable: false),
                    meals_json = table.Column<string>(type: "text", nullable: false),
                    gap_items_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meal_plans", x => x.id);
                    table.ForeignKey(
                        name: "FK_meal_plans_fridges_fridge_id",
                        column: x => x.fridge_id,
                        principalTable: "fridges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    owner_id = table.Column<int>(type: "integer", nullable: false),
                    fridge_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "other")
                },
                constraints: table =>
                {
                    table.PrimaryKey("products_pkey", x => x.id);
                    table.ForeignKey(
                        name: "FK_products_fridges_fridge_id",
                        column: x => x.fridge_id,
                        principalTable: "fridges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "products_owner_id_fkey",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_user_id",
                table: "chat",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_consumption_log_fridge",
                table: "consumption_log",
                column: "fridge_id");

            migrationBuilder.CreateIndex(
                name: "ix_consumption_log_user_consumed_at",
                table: "consumption_log",
                columns: new[] { "user_id", "consumed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_consumption_log_user_status",
                table: "consumption_log",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_email_verification_tokens_hash",
                table: "email_verification_tokens",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_email_verification_tokens_user",
                table: "email_verification_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_fridge_invites_fridge",
                table: "fridge_invites",
                column: "fridge_id");

            migrationBuilder.CreateIndex(
                name: "ix_fridge_invites_hash",
                table: "fridge_invites",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_fridge_members_user",
                table: "fridge_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_fridges_owner",
                table: "fridges",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_meal_plans_fridge_id",
                table: "meal_plans",
                column: "fridge_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nutrition_logs_user_date",
                table: "nutrition_logs",
                columns: new[] { "user_id", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_oauth_logins_provider_provider_user_id",
                table: "oauth_logins",
                columns: new[] { "provider", "provider_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oauth_logins_user",
                table: "oauth_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_fridge",
                table: "products",
                column: "fridge_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_owner_id",
                table: "products",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_recipes_user_id",
                table: "saved_recipes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_shopping_items_fridge",
                table: "shopping_items",
                column: "fridge_id");

            migrationBuilder.CreateIndex(
                name: "ix_shopping_items_user",
                table: "shopping_items",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_shopping_items_user_checked",
                table: "shopping_items",
                columns: new[] { "user_id", "checked" });

            migrationBuilder.CreateIndex(
                name: "users_email_key",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat");

            migrationBuilder.DropTable(
                name: "consumption_log");

            migrationBuilder.DropTable(
                name: "email_verification_tokens");

            migrationBuilder.DropTable(
                name: "email_verifications");

            migrationBuilder.DropTable(
                name: "fridge_invites");

            migrationBuilder.DropTable(
                name: "fridge_members");

            migrationBuilder.DropTable(
                name: "meal_plans");

            migrationBuilder.DropTable(
                name: "nutrition_logs");

            migrationBuilder.DropTable(
                name: "oauth_logins");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "saved_recipes");

            migrationBuilder.DropTable(
                name: "shopping_items");

            migrationBuilder.DropTable(
                name: "fridges");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
