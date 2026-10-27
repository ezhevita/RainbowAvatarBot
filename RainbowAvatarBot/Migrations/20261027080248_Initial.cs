using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RainbowAvatarBot.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    flag_name = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "LGBT"),
                    opacity = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)50),
                    blend_mode = table.Column<int>(type: "integer", nullable: false, defaultValue: 6)
                },
                constraints: table =>
                {
                    table.PrimaryKey("user_settings_pkey", x => x.user_id);
                    table.CheckConstraint("user_settings_blend_mode_check", "blend_mode BETWEEN 0 AND 7");
                    table.CheckConstraint("user_settings_opacity_check", "opacity BETWEEN 0 AND 100");
                    table.CheckConstraint("user_settings_user_id_check", "user_id BETWEEN 1 AND 4503599627370495");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_settings");
        }
    }
}
