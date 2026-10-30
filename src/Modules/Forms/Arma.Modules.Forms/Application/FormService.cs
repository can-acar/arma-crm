using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Comms.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Forms.Domain;
using Arma.Modules.Forms.Infrastructure;
using Arma.Modules.Privacy.Contracts;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Forms.Application;

public sealed record FormView(Guid Id, FormSettings Settings, IReadOnlyList<FormField> Fields, bool CreatesContact, int SubmissionCount, DateTimeOffset? LastSubmission);

public sealed record SubmissionValue(string Label, string? Value);

public sealed record SubmissionView(Guid Id, Guid? ContactId, string? ContactName, IReadOnlyList<SubmissionValue> Values, DateTimeOffset SubmittedAt);

/// <summary>Formdan gelen değerler: alan anahtarı → değer. İşaret kutuları için "on".</summary>
public sealed record FormInput(IReadOnlyDictionary<string, string?> Values, bool EmailConsent, bool SmsConsent, bool ExplicitConsent, string? IpAddress);

public sealed class FormService(FormsDbContext db, IContactRegistry registry, IContactDirectory contacts, IDbContextOutbox outbox, TimeProvider clock)
{
    internal const string SourceModule = "Forms";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<FormView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var forms = await db.Forms.AsNoTracking().OrderBy(f => f.Name).ToListAsync(cancellationToken);
        var stats = await db.Submissions.GroupBy(s => s.FormId)
            .Select(g => new { g.Key, Count = g.Count(), Last = g.Max(s => s.SubmittedAt) })
            .ToDictionaryAsync(x => x.Key, cancellationToken);
        return forms.Select(f => ToView(f, stats.TryGetValue(f.Id, out var s) ? s.Count : 0, stats.TryGetValue(f.Id, out var l) ? l.Last : null)).ToList();
    }

    public async Task<FormView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var form = await db.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        return form is null ? null : ToView(form, await db.Submissions.CountAsync(s => s.FormId == id, cancellationToken), null);
    }

    /// <summary>Kamuya açık sayfa için yayındaki form.</summary>
    public async Task<FormView?> PublishedAsync(string slug, CancellationToken cancellationToken = default)
    {
        var form = await db.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Slug == slug && f.IsActive, cancellationToken);
        return form is null ? null : ToView(form, 0, null);
    }

    public async Task<Result<Guid>> SaveAsync(Guid? id, FormSettings settings, IReadOnlyList<FormField> fields, CancellationToken cancellationToken = default)
    {
        WebForm? form;
        if (id is null)
        {
            var created = WebForm.Create(settings, fields);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            form = created.Value!;
            db.Forms.Add(form);
        }
        else
        {
            form = await db.Forms.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
            if (form is null)
            {
                return FormErrors.NotFound;
            }

            var updated = form.Update(settings, fields);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }
        }

        if (await db.Forms.AnyAsync(f => f.Slug == form.Slug && f.Id != form.Id, cancellationToken))
        {
            return new Error("forms.slug_taken", "Bu adresle başka bir form var.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return form.Id;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var form = await db.Forms.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (form is null)
        {
            return false;
        }

        db.Forms.Remove(form);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<SubmissionView>> SubmissionsAsync(Guid formId, int take = 500, CancellationToken cancellationToken = default)
    {
        var rows = await db.Submissions.AsNoTracking().Where(s => s.FormId == formId).OrderByDescending(s => s.SubmittedAt).Take(take).ToListAsync(cancellationToken);
        var names = await contacts.FindManyAsync([.. rows.Select(r => r.ContactId).OfType<Guid>().Distinct()], cancellationToken);
        return rows.Select(r => new SubmissionView(r.Id, r.ContactId, r.ContactId is { } c && names.TryGetValue(c, out var n) ? n.DisplayName : null,
            JsonSerializer.Deserialize<List<SubmissionValue>>(r.Values, Json) ?? [], r.SubmittedAt)).ToList();
    }

    public async Task<byte[]> SubmissionsCsvAsync(Guid formId, CancellationToken cancellationToken = default)
    {
        var rows = await SubmissionsAsync(formId, 100_000, cancellationToken);
        var labels = rows.SelectMany(r => r.Values.Select(v => v.Label)).Distinct().ToList();
        static string Cell(string? value) => value is null ? "" : value.Contains(';') || value.Contains('"') || value.Contains('\n') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        var output = new StringBuilder();
        output.AppendLine(string.Join(';', new[] { "Tarih", "Kişi" }.Concat(labels).Select(Cell)));
        foreach (var row in rows)
        {
            var cells = new[] { row.SubmittedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), row.ContactName }
                .Concat(labels.Select(l => row.Values.FirstOrDefault(v => v.Label == l)?.Value));
            output.AppendLine(string.Join(';', cells.Select(Cell)));
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(output.ToString())];
    }

    /// <summary>
    /// Kamuya açık formdan gelen yanıtı doğrular ve kaydeder. Ad, soyad ve e-posta eşlenmişse kişi bulunur veya açılır;
    /// etiket, grup, ileti izni ve açık rıza kişiye arka planda işlenir.
    /// </summary>
    public async Task<Result<Guid>> SubmitAsync(string slug, FormInput input, CancellationToken cancellationToken = default)
    {
        var form = await db.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Slug == slug && f.IsActive, cancellationToken);
        if (form is null)
        {
            return FormErrors.NotFound;
        }

        var fields = form.Fields;
        var values = new List<SubmissionValue>();
        foreach (var field in fields)
        {
            var raw = input.Values.TryGetValue(field.Key, out var v) ? v?.Trim() : null;
            if (field.Type == FieldType.Checkbox)
            {
                raw = raw is "on" or "true" ? "Evet" : null;
            }

            if (string.IsNullOrEmpty(raw))
            {
                if (field.Required)
                {
                    return new Error("forms.field_required", $"\"{field.Label}\" alanı zorunlu.");
                }

                values.Add(new(field.Label, null));
                continue;
            }

            var invalid = field.Type switch
            {
                FieldType.Email => !MailAddress.TryCreate(raw, out _),
                FieldType.Number => !decimal.TryParse(raw, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out _),
                FieldType.Date => !DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out _),
                FieldType.Select => !field.Options.Contains(raw),
                FieldType.Phone => raw.Count(char.IsDigit) < 10,
                _ => false,
            };
            if (invalid || raw.Length > (field.Type == FieldType.TextArea ? 5000 : 300))
            {
                return new Error("forms.field_invalid", $"\"{field.Label}\" alanının değeri geçerli değil.");
            }

            values.Add(new(field.Label, raw));
        }

        if (form.ConsentNoticeSlug is not null && !input.ExplicitConsent)
        {
            return new Error("forms.consent_required", "Devam etmek için açık rıza metnini onaylayın.");
        }

        string? Mapped(string map) => fields.Zip(values).FirstOrDefault(x => x.First.MapsTo == map).Second?.Value;

        Guid? contactId = null;
        if (form.CreatesContact)
        {
            var registration = await registry.FindOrCreatePersonAsync(Mapped(FieldMap.FirstName)!, Mapped(FieldMap.LastName)!, Mapped(FieldMap.Email), Mapped(FieldMap.Phone), cancellationToken);
            if (registration.Error is not null)
            {
                return new Error("forms.contact", registration.Error);
            }

            contactId = registration.Id;
        }

        var now = clock.GetUtcNow();
        var submission = FormSubmission.Create(form.Id, contactId, JsonSerializer.Serialize(values, Json), input.IpAddress, now);
        db.Submissions.Add(submission);
        outbox.Enroll(db);
        if (contactId is { } id)
        {
            if (form.TagName is not null || form.GroupId is not null)
            {
                await outbox.PublishAsync(new LabelContact(id, form.TagName, form.GroupId));
            }

            if (form.AskEmailConsent && input.EmailConsent)
            {
                await outbox.PublishAsync(new RecordChannelConsent(id, "email", true, "form"));
            }

            if (form.AskSmsConsent && input.SmsConsent)
            {
                await outbox.PublishAsync(new RecordChannelConsent(id, "sms", true, "form"));
            }

            if (form.ConsentNoticeSlug is { } notice)
            {
                await outbox.PublishAsync(new RecordExplicitConsent(id, notice, true, "form", input.IpAddress));
            }

            var summary = string.Join("\n", fields.Zip(values).Where(x => x.Second.Value is not null && x.First.MapsTo is null).Select(x => $"{x.Second.Label}: {x.Second.Value}"));
            await outbox.PublishAsync(new RecordTimelineEntry([id], ActivityKind.System, $"Form dolduruldu: {form.Title}", summary.Length == 0 ? null : summary,
                now, SourceModule, submission.Id.ToString()));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return submission.Id;
    }

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        foreach (var submission in await db.Submissions.Where(s => s.ContactId == from).ToListAsync(cancellationToken))
        {
            submission.ChangeContact(to);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static FormView ToView(WebForm f, int count, DateTimeOffset? last) =>
        new(f.Id, new FormSettings(f.Name, f.Slug, f.Title, f.Intro, f.SuccessMessage, f.IsActive, f.TagName, f.GroupId, f.AskEmailConsent, f.AskSmsConsent, f.ConsentNoticeSlug, f.InfoNoticeSlug),
            f.Fields, f.CreatesContact, count, last);
}

public static class FormErrors
{
    public static readonly Error NotFound = new("forms.not_found", "Form bulunamadı.");
}

public static class FormsEventHandler
{
    public static Task Handle(ContactsMerged message, FormService forms, CancellationToken cancellationToken) =>
        forms.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
}
