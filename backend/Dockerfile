# syntax=docker/dockerfile:1
# API HMS. Сборка: docker build -t hms-api ./backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig Hms.sln ./
COPY src/ src/
RUN dotnet restore src/Hms.Api/Hms.Api.csproj
RUN dotnet publish src/Hms.Api/Hms.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    TZ=UTC
COPY --from=build /app/publish .
# Каталог загруженных файлов (в compose — volume); пишет только пользователь приложения.
RUN mkdir -p /app/data/files && chown -R $APP_UID /app/data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hms.Api.dll"]
