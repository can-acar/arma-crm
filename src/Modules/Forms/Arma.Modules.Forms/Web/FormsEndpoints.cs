using Arma.Modules.Forms.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Forms.Web;

internal static class FormsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var forms = endpoints.MapGroup("/api/v1/forms").WithTags("Formlar").RequireAuthorization(FormsPermissions.Read);
        forms.MapGet("/", (FormService service, CancellationToken ct) => service.ListAsync(ct));
        forms.MapGet("/{id:guid}", async (FormService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } form ? Results.Ok(form) : Results.NotFound());
        forms.MapGet("/{id:guid}/submissions", (FormService service, Guid id, CancellationToken ct) => service.SubmissionsAsync(id, 500, ct));
        forms.MapGet("/{id:guid}/submissions.csv", async (FormService service, Guid id, CancellationToken ct) =>
            Results.File(await service.SubmissionsCsvAsync(id, ct), "text/csv; charset=utf-8", "form-yanitlari.csv"));
    }
}
