FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DevOpsPortal.sln .
COPY src/DevOpsPortal.Domain/DevOpsPortal.Domain.csproj src/DevOpsPortal.Domain/
COPY src/DevOpsPortal.Application/DevOpsPortal.Application.csproj src/DevOpsPortal.Application/
COPY src/DevOpsPortal.Infrastructure/DevOpsPortal.Infrastructure.csproj src/DevOpsPortal.Infrastructure/
COPY src/DevOpsPortal.Api/DevOpsPortal.Api.csproj src/DevOpsPortal.Api/
COPY tests/DevOpsPortal.Tests/DevOpsPortal.Tests.csproj tests/DevOpsPortal.Tests/
RUN dotnet restore src/DevOpsPortal.Api/DevOpsPortal.Api.csproj

COPY src/ src/
RUN dotnet publish src/DevOpsPortal.Api/DevOpsPortal.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DevOpsPortal.Api.dll"]
