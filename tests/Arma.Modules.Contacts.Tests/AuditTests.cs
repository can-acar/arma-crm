using Arma.Modules.Contacts.Domain;
using Arma.Modules.Contacts.Infrastructure;
using Arma.SharedKernel.Identity;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Tests;

/// <summary>Kayıtların kim tarafından oluşturulup güncellendiği oturumdaki kullanıcıdan gelir.</summary>
public class AuditTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();

    [Fact]
    public async Task Created_and_updated_by_come_from_current_user()
    {
        var databaseName = Guid.NewGuid().ToString();
        var creator = new FixedUser(Guid.CreateVersion7());
        var editor = new FixedUser(Guid.CreateVersion7());
        var ct = TestContext.Current.CancellationToken;

        Guid id;
        await using (var db = CreateContext(databaseName, creator))
        {
            var contact = Contact.CreatePerson("Ayşe", "Yılmaz", null, null, null).Value!;
            db.Contacts.Add(contact);
            await db.SaveChangesAsync(ct);
            id = contact.Id;
        }

        await using (var db = CreateContext(databaseName, editor))
        {
            var contact = await db.Contacts.SingleAsync(c => c.Id == id, ct);
            contact.IsDeleted = true;
            await db.SaveChangesAsync(ct);

            contact.TenantId.ShouldBe(Tenant);
            contact.CreatedBy.ShouldBe(creator.UserId.ToString());
            contact.UpdatedBy.ShouldBe(editor.UserId.ToString());
            contact.UpdatedAt.ShouldNotBeNull();
        }
    }

    private static ContactsDbContext CreateContext(string databaseName, ICurrentUser user) =>
        new(new DbContextOptionsBuilder<ContactsDbContext>().UseInMemoryDatabase(databaseName).Options, new FixedTenant(), user, TimeProvider.System);

    private sealed class FixedTenant : ITenantContext
    {
        public Guid TenantId => Tenant;
    }

    private sealed record FixedUser(Guid? UserId) : ICurrentUser;
}
