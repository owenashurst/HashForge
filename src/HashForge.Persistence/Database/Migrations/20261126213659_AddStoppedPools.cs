using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HashForge.Persistence.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddStoppedPools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoppedPools",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoppedPools", x => x.Name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoppedPools");
        }
    }
}
