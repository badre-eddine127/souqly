# Image de production du service Ordering.
# Construite depuis la racine du dépôt : docker build -f src/Services/Ordering/Souqly.Ordering.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Restauration en couche séparée : elle n'est refaite que si les fichiers de projet changent.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks/Souqly.Chassis/Souqly.Chassis.csproj src/BuildingBlocks/Souqly.Chassis/
COPY src/Services/Ordering/Souqly.Ordering.Domain/Souqly.Ordering.Domain.csproj src/Services/Ordering/Souqly.Ordering.Domain/
COPY src/Services/Ordering/Souqly.Ordering.Application/Souqly.Ordering.Application.csproj src/Services/Ordering/Souqly.Ordering.Application/
COPY src/Services/Ordering/Souqly.Ordering.Infrastructure/Souqly.Ordering.Infrastructure.csproj src/Services/Ordering/Souqly.Ordering.Infrastructure/
COPY src/Services/Ordering/Souqly.Ordering.Api/Souqly.Ordering.Api.csproj src/Services/Ordering/Souqly.Ordering.Api/
RUN dotnet restore src/Services/Ordering/Souqly.Ordering.Api/Souqly.Ordering.Api.csproj

# 2. Compilation et publication.
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Ordering/ src/Services/Ordering/
RUN dotnet publish src/Services/Ordering/Souqly.Ordering.Api/Souqly.Ordering.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

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
  CMD ["dotnet", "Souqly.Ordering.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Souqly.Ordering.Api.dll"]
