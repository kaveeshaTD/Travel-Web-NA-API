using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelWebAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "new"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    replied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_messages", x => x.id);
                    table.CheckConstraint("ck_contact_messages_status", "status IN ('new', 'read', 'replied')");
                });

            migrationBuilder.CreateTable(
                name: "destinations",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    region = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    lat = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    lng = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    best_time = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    travel_time_from_airport = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    featured = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_destinations", x => x.id);
                    table.CheckConstraint("ck_destinations_lat", "lat BETWEEN 5 AND 10");
                    table.CheckConstraint("ck_destinations_lng", "lng BETWEEN 79 AND 82.5");
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    rate_lkr = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: false),
                    source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                    table.CheckConstraint("ck_exchange_rates_currency", "currency IN ('EUR', 'GBP', 'USD')");
                });

            migrationBuilder.CreateTable(
                name: "faqs",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    question = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    answer = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faqs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gallery_images",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    src = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    alt = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    caption = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    aspect = table.Column<decimal>(type: "numeric(5,3)", precision: 5, scale: 3, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gallery_images", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "packages",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tagline = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    duration_days = table.Column<int>(type: "integer", nullable: false),
                    price_lkr = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    image = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    featured = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_packages", x => x.id);
                    table.CheckConstraint("ck_packages_duration_days", "duration_days BETWEEN 1 AND 60");
                    table.CheckConstraint("ck_packages_price_lkr", "price_lkr > 0");
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    image = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "site_settings",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tagline = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    driver_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    driver_role = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    whatsapp_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    address = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    trust_years = table.Column<int>(type: "integer", nullable: false),
                    trust_trips = table.Column<int>(type: "integer", nullable: false),
                    trust_rating = table.Column<decimal>(type: "numeric(2,1)", precision: 2, scale: 1, nullable: false),
                    trust_licensed = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    languages = table.Column<string>(type: "jsonb", nullable: false),
                    licenses = table.Column<string>(type: "jsonb", nullable: false),
                    socials = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_settings", x => x.id);
                    table.CheckConstraint("ck_site_settings_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "travel_tips",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    regions = table.Column<string>(type: "jsonb", nullable: false),
                    info = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_travel_tips", x => x.id);
                    table.CheckConstraint("ck_travel_tips_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "customer"),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "active"),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_role", "role IN ('admin', 'customer')");
                    table.CheckConstraint("ck_users_status", "status IN ('active', 'inactive')");
                });

            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    image = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: false),
                    luggage = table.Column<int>(type: "integer", nullable: false),
                    ac = table.Column<bool>(type: "boolean", nullable: false),
                    price_per_km_lkr = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    price_per_day_lkr = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicles", x => x.id);
                    table.CheckConstraint("ck_vehicles_luggage", "luggage >= 0");
                    table.CheckConstraint("ck_vehicles_price_per_day_lkr", "price_per_day_lkr >= 0");
                    table.CheckConstraint("ck_vehicles_price_per_km_lkr", "price_per_km_lkr >= 0");
                    table.CheckConstraint("ck_vehicles_seats", "seats > 0");
                    table.CheckConstraint("ck_vehicles_type", "type IN ('car', 'minicoach', 'van')");
                });

            migrationBuilder.CreateTable(
                name: "destination_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    destination_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    src = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_destination_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_destination_images_destinations_destination_id",
                        column: x => x.destination_id,
                        principalTable: "destinations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "itinerary_days",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    package_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itinerary_days", x => x.id);
                    table.ForeignKey(
                        name: "fk_itinerary_days_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_destinations",
                columns: table => new
                {
                    package_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    destination_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_destinations", x => new { x.package_id, x.destination_id });
                    table.ForeignKey(
                        name: "fk_package_destinations_destinations_destination_id",
                        column: x => x.destination_id,
                        principalTable: "destinations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_package_destinations_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    package_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    src = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_package_images_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_interests",
                columns: table => new
                {
                    package_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    interest = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_interests", x => new { x.package_id, x.interest });
                    table.CheckConstraint("ck_package_interests_interest", "interest IN ('beach', 'culture', 'nature', 'wildlife')");
                    table.ForeignKey(
                        name: "fk_package_interests_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    package_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    content = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_items", x => x.id);
                    table.CheckConstraint("ck_package_items_kind", "kind IN ('excluded', 'highlight', 'included')");
                    table.ForeignKey(
                        name: "fk_package_items_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_details",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    service_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    detail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_details", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_details_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    file_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    original_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_files", x => x.id);
                    table.CheckConstraint("ck_media_files_size_bytes", "size_bytes <= 5242880");
                    table.ForeignKey(
                        name: "fk_media_files_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_password_reset_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_refresh_tokens_replaced_by_id",
                        column: x => x.replaced_by_id,
                        principalTable: "refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    rating = table.Column<short>(type: "smallint", nullable: false),
                    photo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    trip_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    comment = table.Column<string>(type: "text", nullable: false),
                    review_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "pending"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_reviews_status", "status IN ('pending', 'published', 'rejected')");
                    table.ForeignKey(
                        name: "fk_reviews_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    reference = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    package_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    vehicle_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    travel_date = table.Column<DateOnly>(type: "date", nullable: false),
                    travel_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    flight_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    pickup_location = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    dropoff_location = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    adults = table.Column<int>(type: "integer", nullable: false),
                    children = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    bags = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    notes = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    whatsapp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    trip_plan = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "pending"),
                    price_lkr = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    admin_notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_adults", "adults >= 1");
                    table.CheckConstraint("ck_bookings_bags", "bags >= 0");
                    table.CheckConstraint("ck_bookings_children", "children >= 0");
                    table.CheckConstraint("ck_bookings_price_lkr", "price_lkr IS NULL OR price_lkr >= 0");
                    table.CheckConstraint("ck_bookings_status", "status IN ('cancelled', 'completed', 'confirmed', 'pending')");
                    table.ForeignKey(
                        name: "fk_bookings_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "email_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    to_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    subject = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    delivery_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "queued"),
                    provider_message_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_logs", x => x.id);
                    table.CheckConstraint("ck_email_logs_delivery_status", "delivery_status IN ('failed', 'queued', 'sent')");
                    table.CheckConstraint("ck_email_logs_kind", "kind IN ('booking-cancelled', 'booking-completed', 'booking-confirmed', 'booking-notice', 'booking-received', 'contact-ack', 'custom', 'password-reset', 'reply', 'welcome')");
                    table.ForeignKey(
                        name: "fk_email_logs_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_email_logs_contact_messages_contact_message_id",
                        column: x => x.contact_message_id,
                        principalTable: "contact_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: false),
                    amount_lkr = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    paid_at = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    gateway = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    gateway_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount > 0");
                    table.CheckConstraint("ck_payments_currency", "currency IN ('EUR', 'GBP', 'LKR', 'USD')");
                    table.CheckConstraint("ck_payments_exchange_rate", "exchange_rate > 0");
                    table.CheckConstraint("ck_payments_method", "method IN ('bank_transfer', 'card', 'cash', 'other')");
                    table.CheckConstraint("ck_payments_status", "status IN ('paid', 'pending', 'refunded')");
                    table.ForeignKey(
                        name: "fk_payments_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_email",
                table: "bookings",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_package_id",
                table: "bookings",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_reference",
                table: "bookings",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_service_id",
                table: "bookings",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_status_created_at",
                table: "bookings",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_travel_date",
                table: "bookings",
                column: "travel_date");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_user_id",
                table: "bookings",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_vehicle_id",
                table: "bookings",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_messages_status_created_at",
                table: "contact_messages",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_destination_images_destination_id_sort_order",
                table: "destination_images",
                columns: new[] { "destination_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_destinations_featured",
                table: "destinations",
                column: "featured");

            migrationBuilder.CreateIndex(
                name: "ix_destinations_sort_order",
                table: "destinations",
                column: "sort_order");

            migrationBuilder.CreateIndex(
                name: "ix_email_logs_booking_id",
                table: "email_logs",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_logs_contact_message_id",
                table: "email_logs",
                column: "contact_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_logs_created_at",
                table: "email_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_email_logs_delivery_status_created_at",
                table: "email_logs",
                columns: new[] { "delivery_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_currency_fetched_at",
                table: "exchange_rates",
                columns: new[] { "currency", "fetched_at" });

            migrationBuilder.CreateIndex(
                name: "ix_faqs_sort_order",
                table: "faqs",
                column: "sort_order");

            migrationBuilder.CreateIndex(
                name: "ix_gallery_images_sort_order",
                table: "gallery_images",
                column: "sort_order");

            migrationBuilder.CreateIndex(
                name: "ix_itinerary_days_package_id_day",
                table: "itinerary_days",
                columns: new[] { "package_id", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_files_file_name",
                table: "media_files",
                column: "file_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_files_uploaded_by",
                table: "media_files",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_package_destinations_destination_id",
                table: "package_destinations",
                column: "destination_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_destinations_package_id_position",
                table: "package_destinations",
                columns: new[] { "package_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_package_images_package_id_sort_order",
                table: "package_images",
                columns: new[] { "package_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_package_items_package_id_kind_sort_order",
                table: "package_items",
                columns: new[] { "package_id", "kind", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_packages_active_sort_order",
                table: "packages",
                columns: new[] { "active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_packages_featured",
                table: "packages",
                column: "featured");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_user_id",
                table: "password_reset_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_booking_id",
                table: "payments",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_paid_at",
                table: "payments",
                column: "paid_at");

            migrationBuilder.CreateIndex(
                name: "ix_payments_status",
                table: "payments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expires_at",
                table: "refresh_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_replaced_by_id",
                table: "refresh_tokens",
                column: "replaced_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_status",
                table: "reviews",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_status_review_date",
                table: "reviews",
                columns: new[] { "status", "review_date" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_user_id",
                table: "reviews",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_details_service_id_sort_order",
                table: "service_details",
                columns: new[] { "service_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_services_sort_order",
                table: "services",
                column: "sort_order");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_role",
                table: "users",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "ix_users_status",
                table: "users",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_sort_order",
                table: "vehicles",
                column: "sort_order");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "destination_images");

            migrationBuilder.DropTable(
                name: "email_logs");

            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "faqs");

            migrationBuilder.DropTable(
                name: "gallery_images");

            migrationBuilder.DropTable(
                name: "itinerary_days");

            migrationBuilder.DropTable(
                name: "media_files");

            migrationBuilder.DropTable(
                name: "package_destinations");

            migrationBuilder.DropTable(
                name: "package_images");

            migrationBuilder.DropTable(
                name: "package_interests");

            migrationBuilder.DropTable(
                name: "package_items");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropTable(
                name: "service_details");

            migrationBuilder.DropTable(
                name: "site_settings");

            migrationBuilder.DropTable(
                name: "travel_tips");

            migrationBuilder.DropTable(
                name: "contact_messages");

            migrationBuilder.DropTable(
                name: "destinations");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "packages");

            migrationBuilder.DropTable(
                name: "services");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "vehicles");
        }
    }
}
