FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY src/TenderHack.Domain/TenderHack.Domain.csproj src/TenderHack.Domain/
COPY src/TenderHack.Application/TenderHack.Application.csproj src/TenderHack.Application/
COPY src/TenderHack.Infrastructure/TenderHack.Infrastructure.csproj src/TenderHack.Infrastructure/
COPY src/TenderHack.Web/TenderHack.Web.csproj src/TenderHack.Web/
RUN dotnet restore src/TenderHack.Web/TenderHack.Web.csproj

COPY src/ src/
RUN dotnet publish src/TenderHack.Web/TenderHack.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TenderHack.Web.dll"]
