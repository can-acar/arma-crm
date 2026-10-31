using System.Text;
using Arma.Modules.Invoicing.Application;
using Arma.Modules.Invoicing.Domain;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Invoicing.Web;

internal static class InvoicingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/invoices").WithTags("Faturalar").RequireAuthorization(InvoicingPermissions.Read);

        group.MapGet("/", (InvoiceService service, InvoiceStatus? status, Guid? contactId, string? search, CancellationToken ct) =>
            service.ListAsync(new InvoiceFilter(status, contactId, search), ct));

        group.MapGet("/{id:guid}", async (InvoiceService service, Guid id, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } invoice ? Results.Ok(invoice) : Results.NotFound());

        // UBL-TR dosyası: GİB portalına yüklenir veya entegratöre elle gönderilir.
        group.MapGet("/{id:guid}/xml", async (InvoiceService service, Guid id, CancellationToken ct) =>
            await service.XmlAsync(id, ct) is { } file ? Results.File(Encoding.UTF8.GetBytes(file.Xml), "application/xml; charset=utf-8", file.FileName) : Results.NotFound());

        group.MapPost("/{id:guid}/issue", async (InvoiceService service, Guid id, CancellationToken ct) =>
                NoContent(await service.IssueAsync(id, ct)))
            .RequireAuthorization(InvoicingPermissions.Write);

        group.MapPost("/{id:guid}/transmitted", async (InvoiceService service, Guid id, CancellationToken ct) =>
                NoContent(await service.MarkTransmittedAsync(id, ct)))
            .RequireAuthorization(InvoicingPermissions.Write);

        endpoints.MapGet("/api/v1/einvoice-providers", async (InvoiceService service, CancellationToken ct) =>
                (await service.ProvidersAsync(ct)).Select(p => new
                {
                    p.Info.Key,
                    p.Info.Descriptor.DisplayName,
                    Profiles = InvoiceLabels.Profiles(p.Info.Descriptor.Profiles),
                    p.Info.IsConfigured,
                    p.IsSelected,
                    Missing = p.Info.MissingSettings.Select(m => m.Key),
                }))
            .WithTags("Faturalar")
            .RequireAuthorization(InvoicingPermissions.Settings);
    }

    private static IResult NoContent(Result<Guid> result) => result.IsSuccess
        ? Results.NoContent()
        : result.Error == InvoiceErrors.NotFound ? Results.NotFound()
        : Results.Problem(title: result.Error!.Message, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
}
