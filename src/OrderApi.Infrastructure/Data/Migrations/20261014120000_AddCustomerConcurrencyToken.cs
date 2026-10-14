using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderApi.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // customers.Version maps to the Postgres system column xmin, which every table already has:
            // there is nothing to create. The generated AddColumn was removed on purpose.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: see Up.
        }
    }
}
