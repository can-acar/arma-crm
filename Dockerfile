# Arma CRM kurulum imajı. Aynı imaj hem uygulamayı hem de "migrate" komutunu çalıştırır.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Arma.Host/Arma.Host.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql'in Kerberos desteği için; olmadan her bağlantıda uyarı yazılır.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Arma.Host.dll"]
