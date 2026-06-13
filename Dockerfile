# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Bilboard/Bilboard.csproj", "Bilboard/"]
COPY ["Application/Application.csproj", "Application/"]
RUN dotnet restore "./Bilboard/Bilboard.csproj"
RUN dotnet restore "./Application/Application.csproj"
COPY . .

WORKDIR "/src/Bilboard"
RUN dotnet build "./Application/Application.csproj" -c $BUILD_CONFIGURATION -o /app/Application/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Application/Application.csproj" -c $BUILD_CONFIGURATION -o /app/Application /p:UseAppHost=false

WORKDIR "/src/Bilboard"
RUN dotnet build "./Bilboard/Bilboard.csproj" -c $BUILD_CONFIGURATION -o /app/Bilboard/build

# This stage is used to publish the service project to be copied to the final stage
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Bilboard/Bilboard.csproj" -c $BUILD_CONFIGURATION -o /app/Bilboard /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/Bilboard .
ENTRYPOINT ["dotnet", "Bilboard.dll"]