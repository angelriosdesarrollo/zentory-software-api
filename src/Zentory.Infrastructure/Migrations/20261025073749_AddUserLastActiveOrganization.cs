using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zentory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLastActiveOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "last_active_organization_id",
                table: "users",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_active_organization_id",
                table: "users");
        }
    }
}
