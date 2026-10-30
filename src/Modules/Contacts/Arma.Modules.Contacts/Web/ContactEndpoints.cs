using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Contacts.Web;

internal static class ContactEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/contacts").WithTags("Kişiler").RequireAuthorization(ContactsPermissions.Read);

        group.MapGet("/", (ContactService service, string? search, ContactType? type, Guid? tagId, Guid? groupId, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
            service.ListAsync(new ContactFilter(search, type, tagId, groupId), page, pageSize, ct));

        group.MapGet("/{id:guid}", async (ContactService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } contact ? Results.Ok(contact) : Results.NotFound());

        group.MapGet("/{id:guid}/national-id", async (ContactService service, Guid id, CancellationToken ct) =>
                Results.Ok(new { nationalId = await service.RevealNationalIdAsync(id, ct) }))
            .RequireAuthorization(ContactsPermissions.ViewSensitive);

        group.MapGet("/duplicates", (ContactService service, string? name, string? email, string? phone, string? nationalId, CancellationToken ct) =>
            service.FindDuplicatesAsync(name, email, phone, nationalId, null, ct));

        group.MapPost("/people", async (ContactService service, CreatePersonRequest request, CancellationToken ct) =>
                Created(await service.CreatePersonAsync(request, ct)))
            .RequireAuthorization(ContactsPermissions.Write);

        group.MapPost("/organizations", async (ContactService service, CreateOrganizationRequest request, CancellationToken ct) =>
                Created(await service.CreateOrganizationAsync(request, ct)))
            .RequireAuthorization(ContactsPermissions.Write);

        group.MapPost("/households", async (ContactService service, CreateHouseholdRequest request, CancellationToken ct) =>
                Created(await service.CreateHouseholdAsync(request, ct)))
            .RequireAuthorization(ContactsPermissions.Write);

        group.MapPut("/{id:guid}", async (ContactService service, Guid id, UpdateContactRequest request, CancellationToken ct) =>
                NoContent(await service.UpdateAsync(id, request, ct)))
            .RequireAuthorization(ContactsPermissions.Write);

        group.MapDelete("/{id:guid}", async (ContactService service, Guid id, CancellationToken ct) =>
                NoContent(await service.DeleteAsync(id, ct)))
            .RequireAuthorization(ContactsPermissions.Delete);

        group.MapPost("/{id:guid}/merge/{duplicateId:guid}", async (ContactService service, Guid id, Guid duplicateId, CancellationToken ct) =>
                NoContent(await service.MergeAsync(id, duplicateId, ct)))
            .RequireAuthorization(ContactsPermissions.Delete);

        group.MapPost("/{id:guid}/tags", async (TaxonomyService service, Guid id, TagRequest request, CancellationToken ct) =>
                await service.TagContactAsync(id, request.Name, ct) is { IsSuccess: true } r ? Results.Ok(r.Value) : Results.NotFound())
            .RequireAuthorization(ContactsPermissions.Write);

        group.MapDelete("/{id:guid}/tags/{tagId:guid}", async (TaxonomyService service, Guid id, Guid tagId, CancellationToken ct) =>
                await service.UntagContactAsync(id, tagId, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(ContactsPermissions.Write);

        group.MapPost("/{id:guid}/relationships", async (ContactService service, Guid id, CreateRelationshipRequest request, CancellationToken ct) =>
                Created(await service.AddRelationshipAsync(id, request, ct), "relationships"))
            .RequireAuthorization(ContactsPermissions.Write);

        group.MapGet("/export", async (ContactTransferService service, ICurrentUser user, string? search, ContactType? type, Guid? tagId, Guid? groupId, bool includeNationalId = false, CancellationToken ct = default) =>
            {
                var withIds = includeNationalId && user.HasPermission(ContactsPermissions.ViewSensitive);
                var bytes = await service.ExportAsync(new ContactFilter(search, type, tagId, groupId), withIds, ct);
                return Results.File(bytes, "text/csv; charset=utf-8", $"kisiler-{DateTime.Now:yyyyMMdd-HHmm}.csv");
            })
            .RequireAuthorization(ContactsPermissions.Export);

        group.MapGet("/import-template", () => Results.File(ContactTransferService.Template(), "text/csv; charset=utf-8", "kisi-aktarim-ornek.csv"));

        group.MapPost("/import", async (ContactTransferService service, IFormFile file, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                var result = await service.ImportAsync(stream, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);
            })
            .RequireAuthorization(ContactsPermissions.Write)
            .DisableAntiforgery();

        var tags = endpoints.MapGroup("/api/v1/tags").WithTags("Etiketler").RequireAuthorization(ContactsPermissions.Read);
        tags.MapGet("/", (TaxonomyService service, CancellationToken ct) => service.TagsAsync(ct));
        tags.MapPost("/", async (TaxonomyService service, TagRequest request, CancellationToken ct) =>
                Created(await service.CreateTagAsync(request.Name, request.Color, ct), "tags"))
            .RequireAuthorization(ContactsPermissions.Write);

        var groups = endpoints.MapGroup("/api/v1/groups").WithTags("Gruplar").RequireAuthorization(ContactsPermissions.Read);
        groups.MapGet("/", (TaxonomyService service, CancellationToken ct) => service.GroupsAsync(ct));
        groups.MapPost("/", async (TaxonomyService service, GroupRequest request, CancellationToken ct) =>
                Created(await service.CreateGroupAsync(request.Name, request.Description, ct), "groups"))
            .RequireAuthorization(ContactsPermissions.Write);
        groups.MapPost("/{id:guid}/members", async (TaxonomyService service, Guid id, MembersRequest request, CancellationToken ct) =>
                await service.AddMembersAsync(id, request.ContactIds, ct) is { IsSuccess: true } r ? Results.Ok(new { added = r.Value }) : Results.NotFound())
            .RequireAuthorization(ContactsPermissions.Write);
        groups.MapDelete("/{id:guid}/members/{contactId:guid}", async (TaxonomyService service, Guid id, Guid contactId, CancellationToken ct) =>
                await service.RemoveMemberAsync(id, contactId, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(ContactsPermissions.Write);
    }

    internal sealed record TagRequest(string Name, string? Color);

    internal sealed record GroupRequest(string Name, string? Description);

    internal sealed record MembersRequest(Guid[] ContactIds);

    private static IResult Created(Result<Guid> result, string resource = "contacts") => result.IsSuccess
        ? Results.Created($"/api/v1/{resource}/{result.Value}", new { id = result.Value })
        : Problem(result.Error!);

    private static IResult NoContent(Result<Guid> result) => result.IsSuccess
        ? Results.NoContent()
        : result.Error == Domain.ContactErrors.NotFound ? Results.NotFound() : Problem(result.Error!);

    private static IResult Problem(Error error) =>
        Results.Problem(title: error.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
