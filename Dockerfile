FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine AS build
WORKDIR /build
COPY KCert.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish "KCert.csproj" --no-restore -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:6.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
# COPY wwwroot ./wwwroot
EXPOSE 80
ENTRYPOINT ["dotnet", "KCert.dll"]
