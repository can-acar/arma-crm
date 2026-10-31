using Arma.Modules.Activities.Application;
using Arma.Modules.Activities.Contracts;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Components;

namespace Arma.Modules.Activities.Web;

/// <summary>Aktivite listeleyen sayfaların ortak işlemleri: ekleme/düzenleme penceresi, tamamlama, silme.</summary>
public abstract class ActivityPageBase : ComponentBase
{
    [Inject] protected ActivityService Activities { get; set; } = default!;

    [Inject] protected ICurrentUser CurrentUser { get; set; } = default!;

    protected bool CanWrite { get; private set; }

    protected bool CanDelete { get; private set; }

    protected bool EditorOpen { get; set; }

    protected ActivityView? Editing { get; set; }

    protected ActivityKind NewKind { get; set; } = ActivityKind.Note;

    protected ActivityView? Deleting { get; set; }

    protected string? Error { get; set; }

    protected abstract Task ReloadAsync();

    protected override async Task OnInitializedAsync()
    {
        CanWrite = CurrentUser.HasPermission(ActivitiesPermissions.Write);
        CanDelete = CurrentUser.HasPermission(ActivitiesPermissions.Delete);
        await ReloadAsync();
    }

    protected void New(ActivityKind kind)
    {
        Editing = null;
        NewKind = kind;
        EditorOpen = true;
    }

    protected void Edit(ActivityView activity)
    {
        Editing = activity;
        EditorOpen = true;
    }

    protected void AskDelete(ActivityView activity) => Deleting = activity;

    protected Task CompleteAsync(ActivityView activity) => RunAsync(() => Activities.CompleteAsync(activity.Id));

    protected Task ReopenAsync(ActivityView activity) => RunAsync(() => Activities.ReopenAsync(activity.Id));

    protected async Task DeleteAsync()
    {
        if (Deleting is { } activity)
        {
            await Activities.DeleteAsync(activity.Id);
            Deleting = null;
            await ReloadAsync();
        }
    }

    private async Task RunAsync(Func<Task<SharedKernel.Domain.Result<Guid>>> action)
    {
        var result = await action();
        Error = result.IsSuccess ? null : result.Error!.Message;
        await ReloadAsync();
    }
}
