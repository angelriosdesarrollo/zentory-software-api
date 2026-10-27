using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zentory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveSubscriptionUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subscriptions_user_id",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_user_id",
                schema: "billing",
                table: "subscriptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_user_active",
                schema: "billing",
                table: "subscriptions",
                column: "user_id",
                unique: true,
                filter: "status IN ('active','trialing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subscriptions_user_id",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "ux_subscriptions_user_active",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_user_id",
                schema: "billing",
                table: "subscriptions",
                column: "user_id",
                unique: true);
        }
    }
}
