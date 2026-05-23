# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["TinyFileIO/TinyFileIO.csproj", "TinyFileIO/"]
RUN dotnet restore "./TinyFileIO/TinyFileIO.csproj"
COPY . .
WORKDIR "/src/TinyFileIO"
RUN dotnet build "./TinyFileIO.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TinyFileIO.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
USER $APP_UID
EXPOSE 8080
EXPOSE 8081

LABEL name="TinyFileIO" \
      summary="TinyFileIO is a self-hosted, S3-compatible file storage server built with ASP.NET Core that stores objects on the local filesystem and contains user and web management."

ENV UseStaticAccount=true \
    StaticUser="" \
    StaticPassword=""

WORKDIR /datastore
WORKDIR /app
VOLUME ["/datastore"]
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TinyFileIO.dll"]