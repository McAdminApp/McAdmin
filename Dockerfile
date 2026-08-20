# Bygg körs med repots rot som kontext. Webbappen har en ProjectReference till
# plugin-API:t, så båda projekten måste ligga innanför kontexten.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore mot enbart projektfilerna först, så paketnedladdningen hamnar i ett eget
# lager och överlever ändringar i källkoden.
COPY src/McAdminPlugins.sln src/
COPY src/web/McServerMgmnt.csproj src/web/
COPY src/plugin/McAdminPlugins.csproj src/plugin/
RUN dotnet restore src/McAdminPlugins.sln

COPY src/ src/
# Note: no --no-restore here. The restore above runs against the bare .csproj files,
# before any Razor components or wwwroot exist, and a --no-restore publish on top of it
# drops the framework's static web assets — wwwroot/_framework/blazor.web.js never gets
# published, so every page renders static and nothing interactive works. The restore
# layer above still caches the package download.
RUN dotnet publish src/web/McServerMgmnt.csproj -c Release -o /app/publish


# Plugin-API:t paketeras som en NuGet-fil så att plugin-författare kan kompilera mot
# exakt samma kontrakt som den körande appen laddar. Hämta ut den med:
#   docker build --target package --output type=local,dest=artifacts .
FROM build AS pack
RUN dotnet pack src/plugin/McAdminPlugins.csproj -c Release -o /pack

FROM scratch AS package
COPY --from=pack /pack/ /


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# /app/data holds the SQLite account database, the DataProtection folder holds the
# keys that sign auth cookies and antiforgery tokens. Both must survive a redeploy,
# so mount a volume on each of them (see docker-compose.yml).
#
# /app/addons är släppkatalogen för plugin-assemblies byggda mot McAdminPlugins —
# alltså tillägg till webbappen. Blanda inte ihop den med /app/plugins, som är
# Minecraft-serverns egen plugins-katalog (bind-mountad i compose) och det är
# konfigfilerna *där* som ett plugin får redigera.
RUN mkdir -p /app/data /app/addons /home/app/.aspnet/DataProtection-Keys && \
    chown -R $APP_UID:$APP_UID /app/data /app/addons /home/app/.aspnet

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "McServerMgmnt.dll"]
