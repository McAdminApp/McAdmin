FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY McServerMgmnt.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# /app/data holds the SQLite account database, the DataProtection folder holds the
# keys that sign auth cookies and antiforgery tokens. Both must survive a redeploy,
# so mount a volume on each of them (see docker-compose.yml).
RUN mkdir -p /app/data /home/app/.aspnet/DataProtection-Keys && \
    chown -R $APP_UID:$APP_UID /app/data /home/app/.aspnet

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "McServerMgmnt.dll"]
