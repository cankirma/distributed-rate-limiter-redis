# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY RateLimiter.sln ./
COPY Directory.Build.props ./

COPY src/RateLimiter.Core/RateLimiter.Core.csproj src/RateLimiter.Core/
COPY src/RateLimiter.Infrastructure/RateLimiter.Infrastructure.csproj src/RateLimiter.Infrastructure/
COPY src/RateLimiter.Api/RateLimiter.Api.csproj src/RateLimiter.Api/
COPY benchmarks/RateLimiter.Benchmarks/RateLimiter.Benchmarks.csproj benchmarks/RateLimiter.Benchmarks/
COPY tests/RateLimiter.Core.Tests/RateLimiter.Core.Tests.csproj tests/RateLimiter.Core.Tests/
COPY tests/RateLimiter.IntegrationTests/RateLimiter.IntegrationTests.csproj tests/RateLimiter.IntegrationTests/

RUN dotnet restore RateLimiter.sln

COPY . .

RUN dotnet publish src/RateLimiter.Api/RateLimiter.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "RateLimiter.Api.dll"]
