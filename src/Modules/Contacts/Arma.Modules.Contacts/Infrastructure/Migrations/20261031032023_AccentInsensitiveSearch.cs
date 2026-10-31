using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Contacts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccentInsensitiveSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Arama metni artık Türkçe harfler sadeleştirilerek tutuluyor (Contact.Normalize). Mevcut metin zaten
            // Türkçe küçük harfe çevrilmiş olduğundan harf eşlemesi yeterli. Tablo sahibi FORCE RLS altında
            // kiracı ayarı olmadan satır göremediği için güncelleme süresince zorlama kaldırılır.
            migrationBuilder.Sql("""
                ALTER TABLE contacts.contacts NO FORCE ROW LEVEL SECURITY;
                UPDATE contacts.contacts SET search_text = translate(search_text, 'ışğüöçâîû', 'isguocaiu');
                ALTER TABLE contacts.contacts FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sadeleştirilmiş metin geri çevrilemez; eski sürüm de bu metinde arama yapabilir.
        }
    }
}
