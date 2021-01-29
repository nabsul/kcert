FROM mcr.microsoft.com/dotnet/sdk:5.0-alpine AS build
WORKDIR /build
COPY . .
RUN dotnet build "KCert.csproj" -c Release -o /build/app

FROM mcr.microsoft.com/dotnet/aspnet:5.0-alpine AS final
WORKDIR /app
COPY --from=build /build/app .
COPY wwwroot ./wwwroot
EXPOSE 80
ENTRYPOINT ["dotnet", "KCert.dll"]
