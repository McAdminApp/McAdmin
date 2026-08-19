# Drift

Appen körs som en container bakom en valfri reverse proxy. Allt tillstånd ligger i
två volymer; själva imagen är utbytbar.

## Filer

| Fil                  | Roll |
|----------------------|------|
| `Dockerfile`         | Bygger appen med .NET 10 SDK och kör den på `aspnet:10.0` som användaren `app` (uid 1654). Lyssnar på port 8080 i containern. |
| `docker-compose.yml` | Kör imagen på servern. Här justerar du portar, volymer och nätverk. |
| `Jenkinsfile`        | Bygger imagen, kör `docker compose up -d --force-recreate` och hälsokontrollerar `/login`. |
| `.dockerignore`      | Håller `bin/`, `obj/`, `.git` och lokal data utanför byggkontexten. |

## Kör lokalt

```sh
docker compose up -d --build
```

Appen svarar på <http://localhost:5700>. Första inloggningen är `admin` / `admin`;
kontot är flaggat så att UI:t kräver ett nytt lösenord direkt.

## Vad som måste ligga på volym

* `/app/data` – SQLite-databasen med konton. Anslutningssträngen sätts via
  `ConnectionStrings__AccountsDb` i compose-filen och pekar dit.
* `/home/app/.aspnet/DataProtection-Keys` – nycklarna som signerar auth-cookies och
  antiforgery-tokens. Utan volym loggas alla ut vid varje deploy och Blazor-kretsar
  vägrar återansluta.

## server.properties

`ServerSettingsStore` läser och skriver `server.properties` relativt arbetskatalogen
(`/app`). Peka den mot den riktiga serverns fil genom att avkommentera bind-mounten i
`docker-compose.yml`:

```yaml
- /srv/minecraft/server.properties:/app/server.properties
```

Utan mount rapporterar sidan `IsConnected = false` och visar sin banner i stället för
att spara.

## Bakom reverse proxy

Blazor Server kräver WebSockets — proxya `Upgrade`/`Connection`-headers vidare.
`app.UseHttpsRedirection()` är aktiv i koden men containern exponerar bara HTTP, så
middlewaren hittar ingen HTTPS-port och släpper trafiken vidare orörd. TLS termineras
alltså i proxyn. Vill du att appen själv ska kunna omdirigera, sätt `ASPNETCORE_HTTPS_PORT`.

## Jenkins

Pipen kör på noden med label `deb-slave01` och förutsätter att repot är utcheckat i
jobbets workspace — `docker compose` läser `docker-compose.yml` därifrån, så
volymnamnen prefixas med katalognamnet. Imagen taggas både `:${BUILD_NUMBER}` och
`:latest`; compose-filen refererar `mcservermgmnt:latest` och bygger alltså inte om
det Jenkins redan byggt.

Byt `APP_PORT` i `Jenkinsfile` och portmappningen i `docker-compose.yml` samtidigt om
5700 är upptagen.

## Fallgrop: `--no-restore` i byggsteget

Publicera **inte** med `--no-restore` i Dockerfilen. Restore-steget körs mot enbart
`.csproj`, innan Razor-komponenterna och `wwwroot` finns i kontexten, och en
`--no-restore`-publicering ovanpå det tar inte med ramverkets statiska webbresurser:
`wwwroot/_framework/blazor.web.js` hamnar aldrig i publiceringen. Appen startar och
sidorna renderas, men de renderas statiskt — inget interaktivt fungerar, eftersom
skriptet som startar Blazor-kretsen ger 404.

Snabb kontroll efter en deploy:

```sh
curl -s -o /dev/null -w '%{http_code}' http://localhost:5700/_framework/blazor.web.js; echo
```

Svaret ska vara `200`. Blir det `404` är det den här fallgropen igen.
