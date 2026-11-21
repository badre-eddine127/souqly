# Image de production du site web (HTML, CSS, JavaScript + ASP.NET Core).
# Construite depuis la racine du dépôt : docker build -f src/Web/Souqly.Web/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Restauration en couche séparée : elle n'est refaite que si les fichiers de projet changent.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks/Souqly.Chassis/Souqly.Chassis.csproj src/BuildingBlocks/Souqly.Chassis/
COPY src/Web/Souqly.Web/Souqly.Web.csproj src/Web/Souqly.Web/
RUN dotnet restore src/Web/Souqly.Web/Souqly.Web.csproj

# 2. Compilation et publication.
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Web/Souqly.Web/ src/Web/Souqly.Web/
RUN dotnet publish src/Web/Souqly.Web/Souqly.Web.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# 3. Image finale minimale, utilisateur non root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=0
EXPOSE 8080
COPY --from=build /app .
USER $APP_UID
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=5 \
  CMD ["dotnet", "Souqly.Web.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Souqly.Web.dll"]
