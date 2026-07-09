FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /build
COPY KCert/KCert.csproj .
RUN dotnet restore KCert.csproj
COPY KCert/ .
RUN dotnet publish "KCert.csproj" --no-restore -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
EXPOSE 80
ENTRYPOINT ["dotnet", "KCert.dll"]
