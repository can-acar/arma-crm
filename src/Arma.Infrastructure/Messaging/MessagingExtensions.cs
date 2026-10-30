using System.Reflection;
using JasperFx.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using JasperFx.CodeGeneration.Model;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Arma.Infrastructure.Persistence;

namespace Arma.Infrastructure.Messaging;

public static class MessagingExtensions
{
    public const string Schema = "wolverine";

    /// <summary>
    /// Modüller arası olaylar ve arka plan işleri için Wolverine. Mesajlar PostgreSQL'de saklanır (outbox/inbox);
    /// modül olayı, veritabanı değişikliğiyle aynı işlemde yazılır ve uygulama çökse de kaybolmaz.
    /// İşleyiciler (Handle/Consume metotları) modül derlemelerinden bulunur.
    /// </summary>
    public static WebApplicationBuilder AddArmaMessaging(this WebApplicationBuilder builder, IEnumerable<Assembly> moduleAssemblies)
    {
        var connectionString = builder.Configuration.GetConnectionString(ModuleDbContextExtensions.ConnectionStringName)
            ?? throw new InvalidOperationException($"'ConnectionStrings:{ModuleDbContextExtensions.ConnectionStringName}' yapılandırması eksik.");

        builder.Host.UseWolverine(options =>
        {
            options.ServiceName = "Arma";

            // Modül servisleri fabrika metotlarıyla kaydedilir (DbContext, modül arayüzleri);
            // işleyiciler bunları kapsamdan çözer.
            options.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;

            foreach (var assembly in moduleAssemblies.Distinct())
            {
                options.Discovery.IncludeAssembly(assembly);
            }

            // Bir olayı birden çok modül dinler (kişi birleştirme gibi); her modülün işleyicisi ayrı
            // çalışır, biri hata verirse diğeri tekrar edilmez.
            options.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;

            options.PersistMessagesWithPostgresql(connectionString, Schema);
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.UseDurableLocalQueues();

            // Kurulum paketi tek sunucuda çalışır; düğümler arası liderlik seçimi gerekmez.
            options.Durability.Mode = DurabilityMode.Solo;
        });

        builder.Services.AddResourceSetupOnStartup();
        return builder;
    }
}
