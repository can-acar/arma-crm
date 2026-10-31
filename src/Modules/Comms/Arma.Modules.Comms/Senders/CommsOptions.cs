namespace Arma.Modules.Comms.Senders;

public sealed class CommsOptions
{
    public const string Section = "Comms";

    public EmailOptions Email { get; set; } = new();

    public SmsOptions Sms { get; set; } = new();

    public IysOptions Iys { get; set; } = new();

    /// <summary>Abonelik iptali bağlantısında kullanılan dış adres (https://crm.ornek.org). Boşsa bağlantı eklenmez.</summary>
    public string? PublicUrl { get; set; }
}

public sealed class EmailOptions
{
    /// <summary>none, smtp veya (geliştirme ve testte) test.</summary>
    public string Provider { get; set; } = "none";

    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    /// <summary>auto, starttls, ssl veya none.</summary>
    public string Security { get; set; } = "auto";

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "";

    public string? FromName { get; set; }
}

public sealed class SmsOptions
{
    /// <summary>none, netgsm veya (geliştirme ve testte) test.</summary>
    public string Provider { get; set; } = "none";

    public string UserCode { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>BTK onaylı gönderici adı (mesaj başlığı).</summary>
    public string Header { get; set; } = "";
}

public sealed class IysOptions
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://api.iys.org.tr";

    public string UserName { get; set; } = "";

    public string Password { get; set; } = "";

    public string IysCode { get; set; } = "";

    public string BrandCode { get; set; } = "";
}
