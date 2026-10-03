# syntax=docker/dockerfile:1
# Build context = /backend   (docker build -t techbazar-api ./backend)

# ---- build ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore layer: only the files that affect package resolution, so source edits do not bust the NuGet cache.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/TechBazar.Domain/TechBazar.Domain.csproj               src/TechBazar.Domain/
COPY src/TechBazar.Application/TechBazar.Application.csproj     src/TechBazar.Application/
COPY src/TechBazar.Infrastructure/TechBazar.Infrastructure.csproj src/TechBazar.Infrastructure/
COPY src/TechBazar.Api/TechBazar.Api.csproj                     src/TechBazar.Api/
RUN dotnet restore src/TechBazar.Api/TechBazar.Api.csproj

COPY src/ src/
RUN dotnet publish src/TechBazar.Api/TechBazar.Api.csproj \
      -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- runtime -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# /data/uploads: admin-uploaded images (mount a volume here). /app/logs: Serilog file sink (appsettings.Production.json).
# Both must be writable by the non-root `app` user (UID in $APP_UID, provided by the base image).
RUN mkdir -p /data/uploads /app/logs && chown -R $APP_UID:$APP_UID /data /app/logs

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=0 \
    Storage__RootPath=/data/uploads

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./

USER $APP_UID
EXPOSE 8080
VOLUME ["/data/uploads"]

# The aspnet image ships neither curl nor wget. Instead of installing curl (extra apt layer + attack surface)
# we speak HTTP/1.0 over bash's built-in /dev/tcp. /health returns 200 only when the DB check passes (503 otherwise).
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=5 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]

# Secrets (Jwt__Key, Seed__AdminPassword, ConnectionStrings__DefaultConnection) are supplied at run time via env / secret store.
ENTRYPOINT ["dotnet", "TechBazar.Api.dll"]
