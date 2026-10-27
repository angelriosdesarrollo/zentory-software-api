using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zentory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "master");

            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.CreateTable(
                name: "activity_logs",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_initials = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "arl_risk_levels",
                schema: "master",
                columns: table => new
                {
                    level = table.Column<short>(type: "smallint", nullable: false),
                    description = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(6,5)", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_arl_risk_levels", x => x.level);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    table_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    old_values = table.Column<string>(type: "jsonb", nullable: true),
                    new_values = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cash_flow_entries",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<short>(type: "smallint", nullable: true),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(16,8)", nullable: false),
                    amount_base = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_recurring = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    recurrence_rule = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_flow_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "client_contacts",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    nit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collaborator_access_tokens",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collaborator_access_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collaborator_payout_invoices",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collaborator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    concept = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: true),
                    document_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    document_file_size = table.Column<long>(type: "bigint", nullable: true),
                    document_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    declared_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    SignedByName = table.Column<string>(type: "text", nullable: true),
                    SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    public_token = table.Column<Guid>(type: "uuid", nullable: false),
                    token_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    generated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collaborator_payout_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collaborators",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    hourly_rate = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    monthly_rate = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    pila_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pila_last_verified_period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    arl_risk_level = table.Column<short>(type: "smallint", nullable: true),
                    payout_invoice_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payout_invoice_last_period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    id_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collaborators", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "countries",
                schema: "master",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    timezone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_countries", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "coupon_codes",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    discount_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    applies_to_plan = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    max_redemptions = table.Column<int>(type: "integer", nullable: true),
                    current_redemptions = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "coupon_redemptions",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    discount_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    redeemed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_redemptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credits",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    applied_to_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credits", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "currencies",
                schema: "master",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    symbol = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    decimals = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)2),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currencies", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_gateway = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    gateway_customer_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_verification_tokens",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_verification_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    to_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(16,8)", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_categories",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feature_configs",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_plan = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    legal_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    max_input_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 4000),
                    max_output_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 2000),
                    temperature = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                    model_params = table.Column<string>(type: "jsonb", nullable: false),
                    monthly_req_limit = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "features",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_features", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gateway_routing_rules",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway_key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    legal_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gateway_routing_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "generation_feedback",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usage_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    edit_distance_pct = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    rating = table.Column<short>(type: "smallint", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generation_feedback", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "industries",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_industries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_catalog",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_catalog", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gateway_invoice_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    hosted_invoice_url = table.Column<string>(type: "text", nullable: true),
                    invoice_pdf = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_at = table.Column<DateOnly>(type: "date", nullable: false),
                    due_at = table.Column<DateOnly>(type: "date", nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    amount_paid = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    amount_due = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(16,8)", nullable: true),
                    total_base_currency = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    dian_cufe = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    dian_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    dian_submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dian_response = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    payment_terms = table.Column<string>(type: "text", nullable: true),
                    payment_instructions = table.Column<string>(type: "text", nullable: true),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_viewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_viewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    view_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "models",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    context_window = table.Column<int>(type: "integer", nullable: false),
                    max_output_tokens = table.Column<int>(type: "integer", nullable: false),
                    input_cost_per_1k = table.Column<decimal>(type: "numeric(10,8)", nullable: false),
                    output_cost_per_1k = table.Column<decimal>(type: "numeric(10,8)", nullable: false),
                    supports_streaming = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    supports_vision = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    supports_json_mode = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deprecated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_models", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monthly_usage",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<short>(type: "smallint", nullable: false),
                    month = table.Column<short>(type: "smallint", nullable: false),
                    total_requests = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    successful_requests = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_input_tokens = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    total_output_tokens = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    total_cost_usd = table.Column<decimal>(type: "numeric(12,8)", nullable: false),
                    cache_hits = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_usage", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    body = table.Column<string>(type: "text", nullable: true),
                    action_url = table.Column<string>(type: "text", nullable: true),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oauth_accounts",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    provider_user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    access_token = table.Column<string>(type: "text", nullable: true),
                    refresh_token = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organization_bank_accounts",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    legal_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    account_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    account_holder = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_bank_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organization_invoice_sequences",
                schema: "core",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    year = table.Column<short>(type: "smallint", nullable: false),
                    prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_invoice_sequences", x => new { x.organization_id, x.document_type, x.year });
                });

            migrationBuilder.CreateTable(
                name: "organization_members",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: true),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organization_settings",
                schema: "core",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_settings", x => new { x.organization_id, x.key });
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    legal_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    country = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false, defaultValue: "CO"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.organization_id);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_gateways",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    api_key_env = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    webhook_secret_env = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    public_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    supported_countries = table.Column<string[]>(type: "text[]", nullable: false),
                    supported_currencies = table.Column<string[]>(type: "text[]", nullable: false),
                    webhook_path = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_gateways", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway_pm_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    brand = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    last_four = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    exp_month = table.Column<short>(type: "smallint", nullable: true),
                    exp_year = table.Column<short>(type: "smallint", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_methods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gateway_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    refunded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    refund_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pila_verification_documents",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pila_verification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    file_size = table.Column<long>(type: "bigint", nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pila_verification_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pila_verifications",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collaborator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    document_url = table.Column<string>(type: "text", nullable: true),
                    document_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    document_file_size = table.Column<long>(type: "bigint", nullable: true),
                    document_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    token = table.Column<Guid>(type: "uuid", nullable: false),
                    token_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pila_verifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_compare_items",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    legal_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_empresa_only = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    free_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pro_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    studio_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_order = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_compare_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_features",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    is_highlight = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    badge_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sort_order = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_features", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_limits",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    feature_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    limit_value = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_limits", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_marketing",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tagline = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cta_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_popular = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    features_heading = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_marketing", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price_monthly_usd = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    price_annual_usd = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    gateway_price_id_monthly = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    gateway_price_id_annual = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sort_order = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_collaborators",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collaborator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rate_cost = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    rate_billed = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_collaborators", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_financials",
                schema: "core",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_value = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    total_invoiced = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    total_collected = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    billable_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    total_hours = table.Column<decimal>(type: "numeric(8,1)", nullable: false),
                    billable_hours = table.Column<decimal>(type: "numeric(8,1)", nullable: false),
                    budget_hours = table.Column<decimal>(type: "numeric(8,1)", nullable: false),
                    gross_margin = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    gross_margin_pct = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    net_margin = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    pct_time_elapsed = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    pct_hours_consumed = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    pct_payments_received = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    last_calculated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_financials", x => x.project_id);
                });

            migrationBuilder.CreateTable(
                name: "project_milestones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_milestones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    included_file_ids = table.Column<string[]>(type: "text[]", nullable: false),
                    included_deliverable_ids = table.Column<string[]>(type: "text[]", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_shares", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    milestone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    hours = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    dependencies = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectActivityLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserInitials = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Module = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectActivityLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectBillingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Concept = table.Column<string>(type: "text", nullable: false),
                    Period = table.Column<string>(type: "text", nullable: false),
                    Hours = table.Column<int>(type: "integer", nullable: true),
                    Rate = table.Column<decimal>(type: "numeric", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    InvoiceRef = table.Column<string>(type: "text", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectBillingEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectDeliverables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    MilestoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ApprovedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDeliverables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    FileType = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<string>(type: "text", nullable: false),
                    UploadedBy = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    billing_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contract_value = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    hours_total = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    hours_used = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_templates",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    system_prompt = table.Column<string>(type: "text", nullable: false),
                    user_prompt_tpl = table.Column<string>(type: "text", nullable: true),
                    variables = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "proposal_templates",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    structure = table.Column<string>(type: "jsonb", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "proposals",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    public_token = table.Column<Guid>(type: "uuid", nullable: false),
                    public_token_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    intro_text = table.Column<string>(type: "text", nullable: true),
                    conditions = table.Column<string>(type: "text", nullable: true),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    first_viewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_viewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    view_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    converted_to_project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    converted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "providers",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    base_url = table.Column<string>(type: "text", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quota_overrides",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    monthly_request_limit = table.Column<int>(type: "integer", nullable: true),
                    monthly_token_limit = table.Column<long>(type: "bigint", nullable: true),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_overrides", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_catalog",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    unit_id = table.Column<short>(type: "smallint", nullable: true),
                    default_price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_catalog", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ss_calculation_logs",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collaborator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    income = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: false),
                    total_contribution = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    smlv_used = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    filed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    document_url = table.Column<string>(type: "text", nullable: true),
                    document_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    document_file_size = table.Column<long>(type: "bigint", nullable: true),
                    document_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ss_calculation_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ss_rules",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    effective_year = table.Column<short>(type: "smallint", nullable: false),
                    fund_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contributor_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    employee_pct = table.Column<decimal>(type: "numeric(6,5)", nullable: false),
                    employer_pct = table.Column<decimal>(type: "numeric(6,5)", nullable: false),
                    total_pct = table.Column<decimal>(type: "numeric(6,5)", nullable: false),
                    min_base_smlv = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    max_base_smlv = table.Column<decimal>(type: "numeric(6,4)", nullable: true),
                    smlv_cop = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    arl_level = table.Column<short>(type: "smallint", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ss_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway_subscription_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    gateway_price_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    billing_period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    current_period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    trial_ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    discount_pct = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    dunning_attempt = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    next_dunning_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_events",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    processed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_types",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    applies_to = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "time_entries",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collaborator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    hours = table.Column<decimal>(type: "numeric(4,1)", nullable: false),
                    rate_cost = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    rate_billed = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    billable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    billed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unit_of_measure",
                schema: "master",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unit_of_measure", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usage_logs",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    context_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    context_id = table.Column<Guid>(type: "uuid", nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    output_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    cost_usd = table.Column<decimal>(type: "numeric(12,8)", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    served_from_cache = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    cache_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    google_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    terms_accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    terms_accepted_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_events",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_gateway = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    gateway_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    processed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organization_integrations",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    connected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    connected_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    connected_as = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    external_account_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    external_workspace_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    access_token_encrypted = table.Column<string>(type: "text", nullable: true),
                    refresh_token_encrypted = table.Column<string>(type: "text", nullable: true),
                    token_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    scopes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_integrations", x => x.id);
                    table.ForeignKey(
                        name: "FK_organization_integrations_integration_catalog_integration_id",
                        column: x => x.integration_id,
                        principalSchema: "core",
                        principalTable: "integration_catalog",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_items",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    unit_id = table.Column<short>(type: "smallint", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    tax_type_id = table.Column<short>(type: "smallint", nullable: true),
                    discount_pct = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    sort_order = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_invoice_items_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "core",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_payments",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    paid_at = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_payments", x => x.id);
                    table.ForeignKey(
                        name: "FK_invoice_payments_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "core",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "proposal_items",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    unit_id = table.Column<short>(type: "smallint", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    discount_pct = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    sort_order = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_proposal_items_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalSchema: "core",
                        principalTable: "proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposal_sections",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    content = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_encrypted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_sections", x => x.id);
                    table.ForeignKey(
                        name: "FK_proposal_sections_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalSchema: "core",
                        principalTable: "proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "integration_catalog",
                columns: new[] { "id", "created_at", "description", "is_enabled", "name", "sort_order", "updated_at" },
                values: new object[,]
                {
                    { "figma", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Adjunta entregables de diseño directamente en propuestas", true, "Figma", 4, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "github", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Vincula repositorios con proyectos y time entries", true, "GitHub", 3, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "google-meet", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Videollamadas integradas en propuestas y proyectos", true, "Google Meet", 1, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "slack", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Notificaciones de proyectos e invoices en tu workspace", true, "Slack", 2, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_org_entity",
                schema: "core",
                table: "activity_logs",
                columns: new[] { "organization_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_org_occurred",
                schema: "core",
                table: "activity_logs",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_org_created",
                schema: "core",
                table: "audit_log",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_flow_entries_org_date",
                schema: "core",
                table: "cash_flow_entries",
                columns: new[] { "organization_id", "transaction_date" });

            migrationBuilder.CreateIndex(
                name: "ix_client_contacts_client_id",
                schema: "core",
                table: "client_contacts",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_clients_org_id",
                table: "clients",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_clients_org_name",
                table: "clients",
                columns: new[] { "organization_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_collaborator_access_tokens_email",
                schema: "core",
                table: "collaborator_access_tokens",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_collaborator_access_tokens_hash",
                schema: "core",
                table: "collaborator_access_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payout_invoices_collaborator_period",
                schema: "core",
                table: "collaborator_payout_invoices",
                columns: new[] { "collaborator_id", "period" });

            migrationBuilder.CreateIndex(
                name: "ix_payout_invoices_token",
                schema: "core",
                table: "collaborator_payout_invoices",
                column: "public_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collaborators_org_id",
                schema: "core",
                table: "collaborators",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_collaborators_org_status",
                schema: "core",
                table: "collaborators",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_codes_code",
                schema: "billing",
                table: "coupon_codes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemptions_coupon_org",
                schema: "billing",
                table: "coupon_redemptions",
                columns: new[] { "coupon_id", "organization_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_billing_customers_gateway_id",
                schema: "billing",
                table: "customers",
                column: "gateway_customer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_billing_customers_org_id",
                schema: "billing",
                table: "customers",
                column: "organization_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_verification_tokens_hash",
                schema: "core",
                table: "email_verification_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_master_exchange_rates_pair_date",
                schema: "master",
                table: "exchange_rates",
                columns: new[] { "from_currency", "to_currency", "rate_date" });

            migrationBuilder.CreateIndex(
                name: "ix_master_expense_categories_slug",
                schema: "master",
                table: "expense_categories",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_feature_configs_feature",
                schema: "ai",
                table: "feature_configs",
                column: "feature_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_features_key",
                schema: "ai",
                table: "features",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gateway_routing_active",
                schema: "billing",
                table: "gateway_routing_rules",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_ai_generation_feedback_usage_log_id",
                schema: "ai",
                table: "generation_feedback",
                column: "usage_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_master_industries_slug",
                schema: "master",
                table: "industries",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_items_invoice_id",
                schema: "core",
                table: "invoice_items",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_payments_invoice_id",
                schema: "core",
                table: "invoice_payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_client_id",
                schema: "core",
                table: "invoices",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_org_id",
                schema: "core",
                table: "invoices",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_org_number",
                schema: "core",
                table: "invoices",
                columns: new[] { "organization_id", "invoice_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_org_status",
                schema: "core",
                table: "invoices",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_models_model_id",
                schema: "ai",
                table: "models",
                column: "model_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_monthly_usage_org_feature_period",
                schema: "ai",
                table: "monthly_usage",
                columns: new[] { "organization_id", "feature_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_read",
                schema: "core",
                table: "notifications",
                columns: new[] { "user_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_oauth_accounts_provider_user",
                schema: "core",
                table: "oauth_accounts",
                columns: new[] { "provider", "provider_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_bank_accounts_org_id",
                schema: "core",
                table: "organization_bank_accounts",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_integrations_org_id",
                schema: "core",
                table: "organization_integrations",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_integrations_org_integration",
                schema: "core",
                table: "organization_integrations",
                columns: new[] { "organization_id", "integration_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_integrations_integration_id",
                schema: "core",
                table: "organization_integrations",
                column: "integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_members_org_user",
                schema: "core",
                table: "organization_members",
                columns: new[] { "organization_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pwd_reset_tokens_hash",
                schema: "core",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateways_key",
                schema: "billing",
                table: "payment_gateways",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_gateway_pm_id",
                schema: "billing",
                table: "payment_methods",
                column: "gateway_pm_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pila_verification_documents_verification_id",
                schema: "core",
                table: "pila_verification_documents",
                column: "pila_verification_id");

            migrationBuilder.CreateIndex(
                name: "ix_pila_verifications_collaborator_period",
                schema: "core",
                table: "pila_verifications",
                columns: new[] { "collaborator_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pila_verifications_token",
                schema: "core",
                table: "pila_verifications",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_compare_lookup",
                schema: "billing",
                table: "plan_compare_items",
                columns: new[] { "legal_type", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_plan_features_lookup",
                schema: "billing",
                table: "plan_features",
                columns: new[] { "plan_id", "legal_type", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_plan_limits_plan_account_feature",
                schema: "billing",
                table: "plan_limits",
                columns: new[] { "plan_id", "legal_type", "feature_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_marketing_plan_account",
                schema: "billing",
                table: "plan_marketing",
                columns: new[] { "plan_id", "legal_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_billing_plans_name",
                schema: "billing",
                table: "plans",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_collaborators_project_collaborator",
                schema: "core",
                table: "project_collaborators",
                columns: new[] { "project_id", "collaborator_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_milestones_org_id",
                table: "project_milestones",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_milestones_project_id",
                table: "project_milestones",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_shares_project_id",
                table: "project_shares",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_shares_token",
                table: "project_shares",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_tasks_org_id",
                table: "project_tasks",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_tasks_project_id",
                table: "project_tasks",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_client_id",
                table: "projects",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_org_id",
                table: "projects",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_prompt_templates_feature_version",
                schema: "ai",
                table: "prompt_templates",
                columns: new[] { "feature_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposal_items_proposal_id",
                schema: "core",
                table: "proposal_items",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposal_sections_proposal_id",
                schema: "core",
                table: "proposal_sections",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_client_id",
                schema: "core",
                table: "proposals",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_org_id",
                schema: "core",
                table: "proposals",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_public_token",
                schema: "core",
                table: "proposals",
                column: "public_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_providers_name",
                schema: "ai",
                table: "providers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_quota_overrides_org_id",
                schema: "ai",
                table: "quota_overrides",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token",
                table: "refresh_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_catalog_org_id",
                schema: "core",
                table: "service_catalog",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_ss_calc_logs_org_period",
                schema: "core",
                table: "ss_calculation_logs",
                columns: new[] { "organization_id", "period" });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_user_id",
                schema: "billing",
                table: "subscriptions",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_system_events_org_processed",
                schema: "core",
                table: "system_events",
                columns: new[] { "organization_id", "processed" });

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_collaborator_id",
                schema: "core",
                table: "time_entries",
                column: "collaborator_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_org_date",
                schema: "core",
                table: "time_entries",
                columns: new[] { "organization_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_org_id",
                schema: "core",
                table: "time_entries",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_project_id",
                schema: "core",
                table: "time_entries",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_logs_org_created",
                schema: "ai",
                table: "usage_logs",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_logs_org_feature",
                schema: "ai",
                table: "usage_logs",
                columns: new[] { "organization_id", "feature_id" });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_google_id",
                table: "users",
                column: "google_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_org_id",
                table: "users",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_gateway_event",
                schema: "billing",
                table: "webhook_events",
                columns: new[] { "payment_gateway", "gateway_event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_logs",
                schema: "core");

            migrationBuilder.DropTable(
                name: "arl_risk_levels",
                schema: "master");

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "core");

            migrationBuilder.DropTable(
                name: "cash_flow_entries",
                schema: "core");

            migrationBuilder.DropTable(
                name: "client_contacts",
                schema: "core");

            migrationBuilder.DropTable(
                name: "clients");

            migrationBuilder.DropTable(
                name: "collaborator_access_tokens",
                schema: "core");

            migrationBuilder.DropTable(
                name: "collaborator_payout_invoices",
                schema: "core");

            migrationBuilder.DropTable(
                name: "collaborators",
                schema: "core");

            migrationBuilder.DropTable(
                name: "countries",
                schema: "master");

            migrationBuilder.DropTable(
                name: "coupon_codes",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "coupon_redemptions",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "credits",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "currencies",
                schema: "master");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "email_verification_tokens",
                schema: "core");

            migrationBuilder.DropTable(
                name: "exchange_rates",
                schema: "master");

            migrationBuilder.DropTable(
                name: "expense_categories",
                schema: "master");

            migrationBuilder.DropTable(
                name: "feature_configs",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "features",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "gateway_routing_rules",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "generation_feedback",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "industries",
                schema: "master");

            migrationBuilder.DropTable(
                name: "invoice_items",
                schema: "core");

            migrationBuilder.DropTable(
                name: "invoice_payments",
                schema: "core");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "models",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "monthly_usage",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "core");

            migrationBuilder.DropTable(
                name: "oauth_accounts",
                schema: "core");

            migrationBuilder.DropTable(
                name: "organization_bank_accounts",
                schema: "core");

            migrationBuilder.DropTable(
                name: "organization_integrations",
                schema: "core");

            migrationBuilder.DropTable(
                name: "organization_invoice_sequences",
                schema: "core");

            migrationBuilder.DropTable(
                name: "organization_members",
                schema: "core");

            migrationBuilder.DropTable(
                name: "organization_settings",
                schema: "core");

            migrationBuilder.DropTable(
                name: "organizations");

            migrationBuilder.DropTable(
                name: "password_reset_tokens",
                schema: "core");

            migrationBuilder.DropTable(
                name: "payment_gateways",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "pila_verification_documents",
                schema: "core");

            migrationBuilder.DropTable(
                name: "pila_verifications",
                schema: "core");

            migrationBuilder.DropTable(
                name: "plan_compare_items",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "plan_features",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "plan_limits",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "plan_marketing",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "plans",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "project_collaborators",
                schema: "core");

            migrationBuilder.DropTable(
                name: "project_financials",
                schema: "core");

            migrationBuilder.DropTable(
                name: "project_milestones");

            migrationBuilder.DropTable(
                name: "project_shares");

            migrationBuilder.DropTable(
                name: "project_tasks");

            migrationBuilder.DropTable(
                name: "ProjectActivityLogs");

            migrationBuilder.DropTable(
                name: "ProjectBillingEntries");

            migrationBuilder.DropTable(
                name: "ProjectDeliverables");

            migrationBuilder.DropTable(
                name: "ProjectFiles");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "prompt_templates",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "proposal_items",
                schema: "core");

            migrationBuilder.DropTable(
                name: "proposal_sections",
                schema: "core");

            migrationBuilder.DropTable(
                name: "proposal_templates",
                schema: "core");

            migrationBuilder.DropTable(
                name: "providers",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "quota_overrides",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "service_catalog",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ss_calculation_logs",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ss_rules",
                schema: "master");

            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "system_events",
                schema: "core");

            migrationBuilder.DropTable(
                name: "tax_types",
                schema: "master");

            migrationBuilder.DropTable(
                name: "time_entries",
                schema: "core");

            migrationBuilder.DropTable(
                name: "unit_of_measure",
                schema: "master");

            migrationBuilder.DropTable(
                name: "usage_logs",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "webhook_events",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "core");

            migrationBuilder.DropTable(
                name: "integration_catalog",
                schema: "core");

            migrationBuilder.DropTable(
                name: "proposals",
                schema: "core");
        }
    }
}
