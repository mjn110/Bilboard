# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Bilboard/Bilboard.csproj", "Bilboard/"]
COPY ["Presentation/Presentation.csproj", "Presentation/"]
COPY ["Application/Application.csproj", "Application/"]
COPY ["Domain/Domain.csproj", "Domain/"]
COPY ["Infrastructure/Infrastructure.csproj", "Infrastructure/"]
RUN dotnet restore "./Bilboard/Bilboard.csproj"
RUN dotnet restore "./Presentation/Presentation.csproj"
COPY . .

WORKDIR "/src/Presentation"
RUN dotnet build "./Presentation.csproj" -c $BUILD_CONFIGURATION -o /app/build/api
WORKDIR "/src/Bilboard"
RUN dotnet build "./Bilboard.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src/Presentation"
RUN dotnet publish "./Presentation.csproj" -c $BUILD_CONFIGURATION -o /app/publish/api /p:UseAppHost=false

WORKDIR "/src/Bilboard"
RUN dotnet publish "./Bilboard.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production
FROM base AS final
WORKDIR /app

# Install supervisord to run both processes
RUN apt-get update && apt-get install -y supervisor && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish/api /app/api
COPY --from=publish /app/publish /app
COPY supervisord.conf /etc/supervisor/conf.d/supervisord.conf

CMD ["/usr/bin/supervisord", "-c", "/etc/supervisor/conf.d/supervisord.conf"]