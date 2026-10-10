FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . ./
RUN dotnet restore src/LedgerPilot.Api/LedgerPilot.Api.csproj --locked-mode
RUN dotnet publish src/LedgerPilot.Api/LedgerPilot.Api.csproj -c Release --no-restore -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LedgerPilot.Api.dll"]
