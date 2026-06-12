# ── Stage 1: base runtime image ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# ── Stage 2: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files and restore
COPY ["Bilboard/Bilboard.csproj",         "Bilboard/"]
COPY ["Presentation/Presentation.csproj", "Presentation/"]
COPY ["Application/Application.csproj",   "Application/"]
COPY ["Domain/Domain.csproj",             "Domain/"]
COPY ["Infrastructure/Infrastructure.csproj", "Infrastructure/"]

RUN dotnet restore "./Bilboard/Bilboard.csproj"
RUN dotnet restore "./Presentation/Presentation.csproj"

# Copy all source and build both projects
COPY . .

RUN dotnet publish "./Bilboard/Bilboard.csproj" \
    -c $BUILD_CONFIGURATION -o /app/publish/Bilboard /p:UseAppHost=false

RUN dotnet publish "./Presentation/Presentation.csproj" \
    -c $BUILD_CONFIGURATION -o /app/publish/Presentation /p:UseAppHost=false

# ── Stage 3: final runtime image ──────────────────────────────────────────────
FROM base AS final
WORKDIR /app

# Install supervisord to manage both processes
RUN apt-get update && apt-get install -y supervisor && rm -rf /var/lib/apt/lists/*

# Copy both published apps
COPY --from=build /app/publish/Bilboard    ./Bilboard
COPY --from=build /app/publish/Presentation ./Presentation

# Copy supervisor config
COPY supervisord.conf /etc/supervisor/conf.d/supervisord.conf

CMD ["/usr/bin/supervisord", "-c", "/etc/supervisor/conf.d/supervisord.conf"]