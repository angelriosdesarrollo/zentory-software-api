using Microsoft.EntityFrameworkCore.Migrations;
using Zentory.Domain.Constants;

#nullable disable

namespace Zentory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueActiveMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sentencia de datos antepuesta a mano (ADR-0007 §5, architecture.md §3 M4b, SR-0018.8): el índice único no
            // cabe si hay dos membresías vigentes del mismo (org, user). De cada grupo se conserva una y las demás quedan
            // borradas: gana la membresía owner del Organization.OwnerId (la del owner nunca se borra) y, si no la hay,
            // la más antigua. Sin duplicados no cambia ninguna fila. Down no la revierte (las filas quedan borradas).
            migrationBuilder.Sql($"""
                UPDATE core.organization_members AS m
                SET deleted_at = now(), updated_at = now()
                FROM (
                    SELECT om.id,
                           row_number() OVER (
                               PARTITION BY om.organization_id, om.user_id
                               ORDER BY (om.user_id = o.owner_id AND om.role = '{OrgRole.Owner}') IS TRUE DESC,
                                        om.created_at, om.id) AS rn
                    FROM core.organization_members AS om
                    LEFT JOIN organizations AS o ON o.organization_id = om.organization_id
                    WHERE om.deleted_at IS NULL
                ) AS ranked
                WHERE m.id = ranked.id AND ranked.rn > 1;
                """);

            migrationBuilder.DropIndex(
                name: "ix_org_members_org_user",
                schema: "core",
                table: "organization_members");

            migrationBuilder.CreateIndex(
                name: "ix_org_members_org_user",
                schema: "core",
                table: "organization_members",
                columns: new[] { "organization_id", "user_id" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_org_members_org_user",
                schema: "core",
                table: "organization_members");

            migrationBuilder.CreateIndex(
                name: "ix_org_members_org_user",
                schema: "core",
                table: "organization_members",
                columns: new[] { "organization_id", "user_id" });
        }
    }
}
