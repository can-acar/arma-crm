using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Contacts.Application;

/// <summary>Kişiler modülünün dinlediği mesajlar.</summary>
public static class ContactsEventHandler
{
    public static async Task Handle(LabelContact message, TaxonomyService taxonomy, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(message.TagName))
        {
            await taxonomy.TagContactAsync(message.ContactId, message.TagName, cancellationToken);
        }

        if (message.GroupId is { } groupId)
        {
            await taxonomy.AddMembersAsync(groupId, [message.ContactId], cancellationToken);
        }
    }
}
