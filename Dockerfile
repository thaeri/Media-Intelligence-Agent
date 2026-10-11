FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS restore
WORKDIR /src

COPY MediaIntelligence.slnx ./
COPY src/MediaIntelligence.Api/MediaIntelligence.Api.csproj src/MediaIntelligence.Api/
COPY src/MediaIntelligence.Core/MediaIntelligence.Core.csproj src/MediaIntelligence.Core/
COPY tests/MediaIntelligence.Tests/MediaIntelligence.Tests.csproj tests/MediaIntelligence.Tests/
RUN dotnet restore MediaIntelligence.slnx

FROM restore AS build
COPY . .
RUN dotnet build MediaIntelligence.slnx -c Release --no-restore
RUN dotnet publish src/MediaIntelligence.Api/MediaIntelligence.Api.csproj \
    -c Release --no-build --no-restore -o /app/publish /p:UseAppHost=false

FROM build AS test
ENTRYPOINT ["dotnet", "test", "MediaIntelligence.slnx", "-c", "Release", "--no-build", "--no-restore"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "MediaIntelligence.Api.dll"]
