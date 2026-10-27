using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zentory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationOwnershipTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organization_ownership_transfers",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    preview_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_ownership_transfers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_org_ownership_transfers_org_pending",
                schema: "core",
                table: "organization_ownership_transfers",
                column: "organization_id",
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_org_ownership_transfers_to_user_status",
                schema: "core",
                table: "organization_ownership_transfers",
                columns: new[] { "to_user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_ownership_transfers",
                schema: "core");
        }
    }
}
