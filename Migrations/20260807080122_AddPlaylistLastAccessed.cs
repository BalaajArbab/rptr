using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace rptr.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistLastAccessed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastAccessed",
                table: "Playlists",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [Playlists] SET [LastAccessed] = [CreatedAt]");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastAccessed",
                table: "Playlists",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAccessed",
                table: "Playlists");
        }
    }
}
