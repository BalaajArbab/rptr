using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace rptr.Migrations
{
    /// <inheritdoc />
    public partial class AddSongTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SongTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlaylistId = table.Column<int>(type: "int", nullable: false),
                    SongId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SongName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrimaryArtistName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SongTags_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SongTags_PlaylistId_SongId",
                table: "SongTags",
                columns: new[] { "PlaylistId", "SongId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SongTags");
        }
    }
}
