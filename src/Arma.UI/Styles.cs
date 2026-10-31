namespace Arma.UI;

/// <summary>
/// Bileşenlerin sınıf dizileri. Sunucu tarafı formlarda (static SSR) <c>InputText</c> gibi yerleşik
/// form bileşenleri kullanılırken aynı görünümü vermek için de kullanılır.
/// </summary>
public static class Styles
{
    public const string Input =
        "file:text-foreground placeholder:text-muted-foreground selection:bg-primary selection:text-primary-foreground " +
        "dark:bg-input/30 border-input flex h-9 w-full min-w-0 rounded-md border bg-transparent px-3 py-1 text-base shadow-xs " +
        "transition-[color,box-shadow] outline-none file:inline-flex file:h-7 file:border-0 file:bg-transparent file:text-sm " +
        "file:font-medium disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50 md:text-sm " +
        "focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px] aria-invalid:ring-destructive/20 " +
        "dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive";

    public const string Checkbox =
        "border-input dark:bg-input/30 accent-primary size-4 shrink-0 rounded-[4px] border shadow-xs outline-none " +
        "focus-visible:ring-ring/50 focus-visible:ring-[3px] disabled:cursor-not-allowed disabled:opacity-50";

    public const string Select =
        "border-input dark:bg-input/30 flex h-9 w-full min-w-0 appearance-none rounded-md border bg-transparent px-3 py-1 " +
        "pr-8 text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px] " +
        "disabled:cursor-not-allowed disabled:opacity-50 bg-[length:16px] bg-[right_0.5rem_center] bg-no-repeat " +
        "bg-[url(\"data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='%23737373' stroke-width='2'%3e%3cpath d='m6 9 6 6 6-6'/%3e%3c/svg%3e\")]";

    public const string Textarea =
        "border-input placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-ring/50 dark:bg-input/30 " +
        "flex field-sizing-content min-h-16 w-full rounded-md border bg-transparent px-3 py-2 text-base shadow-xs " +
        "transition-[color,box-shadow] outline-none focus-visible:ring-[3px] disabled:cursor-not-allowed disabled:opacity-50 md:text-sm";
}
