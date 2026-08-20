# The build context is the repository root. The web app has a ProjectReference to the
# plugin API, so both projects have to sit inside the context.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the bare project files first, so the package download lands in a layer
# of its own and survives edits to the source. There is no solution file; restoring the
# web project pulls in the API project through its ProjectReference.
COPY API/src/McAdminPlugins.csproj API/src/
COPY Web/src/McServerMgmnt.csproj Web/src/
RUN dotnet restore Web/src/McServerMgmnt.csproj

COPY API/ API/
COPY Web/ Web/
# Note: no --no-restore here. The restore above runs against the bare .csproj files,
# before any Razor components or wwwroot exist, and a --no-restore publish on top of it
# drops the framework's static web assets — wwwroot/_framework/blazor.web.js never gets
# published, so every page renders static and nothing interactive works. The restore
# layer above still caches the package download.
RUN dotnet publish Web/src/McServerMgmnt.csproj -c Release -o /app/publish


# The plugin API is exported as a loose .dll — plugin authors download it and reference
# it themselves. It comes out of the publish output, so it is the exact same file the
# running app loads, and therefore the same contract. Extract it with:
#   docker build --target api --output type=local,dest=artifacts .
FROM scratch AS api
COPY --from=build /app/publish/McAdminPlugins.dll /


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# /app/data holds the SQLite account database, the DataProtection folder holds the
# keys that sign auth cookies and antiforgery tokens. Both must survive a redeploy,
# so mount a volume on each of them (see docker-compose.yml).
#
# /app/addons is the drop-in folder for plugin assemblies built against McAdminPlugins —
# extensions to the web app. Do not confuse it with /app/plugins, which is the Minecraft
# server's own plugins folder (bind-mounted in compose); it is the config files *there*
# that a plugin is allowed to edit.
RUN mkdir -p /app/data /app/addons /home/app/.aspnet/DataProtection-Keys && \
    chown -R $APP_UID:$APP_UID /app/data /app/addons /home/app/.aspnet

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "McServerMgmnt.dll"]
