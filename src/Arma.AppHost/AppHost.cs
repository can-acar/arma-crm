// Yerel geliştirme ortamı: PostgreSQL konteynerini ve Arma.Host'u birlikte başlatır,
// izleri ve logları Aspire panelinde gösterir. Çalıştırmak için: dotnet run --project src/Arma.AppHost
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .WithDataVolume("arma-postgres-data");

var database = postgres.AddDatabase("arma");

builder.AddProject<Projects.Arma_Host>("arma-host")
    .WithReference(database)
    .WaitFor(database)
    .WithEnvironment("Arma__TenantId", "0193a6f0-0000-7000-8000-000000000001")
    .WithEnvironment("Arma__MigrateOnStartup", "true");

builder.Build().Run();
