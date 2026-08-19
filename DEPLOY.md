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

### Skrivrätt på server.properties

Containern kör som uid 1654 (`app`) medan filen ägs av Minecraft-användaren, så en
ren mount ger `Access to the path '/app/server.properties' is denied.` när du sparar.
Läsning fungerar, vilket gör att sidan ser helt frisk ut tills första sparningen.

Gör filen gruppskrivbar och låt containern gå med i samma grupp:

```sh
stat -c '%U %G %u %g %a' /opt/minecraft/server.properties   # t.ex. minecraft minecraft 1000 1000 644
sudo chmod g+w /opt/minecraft/server.properties
```

och i `docker-compose.yml`:

```yaml
group_add:
  - "1000"   # gid från stat ovan
```

Ägaren är kvar som Minecraft-användaren, så servern kan fortsätta skriva sin egen fil.

Sätt **inte** `user:` för att lösa det. `/app/data` och DataProtection-katalogen ägs av
1654 i imagen; med en annan uid startar appen inte alls utan dör på
`SQLite Error 14: 'unable to open database file'`.

Om Minecraft-servern någon gång tar bort och återskapar `server.properties` återgår
rättigheterna till 644 och sparningen börjar neka igen — kör `chmod g+w` på nytt.

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

## Serverstyrning (RCON + Docker)

Konsolsidan drivs av `RconServerController` i `Services/Rcon/`. Den registreras bara när
`McServer:Host` och `McServer:RconPassword` är satta — annars ligger
`PlaceholderServerController` kvar, så en utcheckning utan server bakom sig fortfarande
startar.

Arbetet är uppdelat på tre källor, eftersom RCON inte räcker till allt:

| Funktion | Källa | Varför |
|---|---|---|
| Kommandon, spelarlista, stopp | RCON | Det protokollet servern lyssnar på. |
| Start och omstart | Docker-socketen | RCON dör med servern. En nedstängd server har ingen lyssnare kvar att be om start. |
| Loggpanelen | `logs/latest.log` | RCON returnerar bara svar på kommandon, aldrig serverns egen utskrift. |
| Namn, platser, port | `server.properties` | Redan monterad för inställningssidan; `motd` blir rubriken. |
| Version | Loggens startbanner | Vanilla har inget versionskommando. |

### Konfiguration

Allt under `McServer` i appsettings, eller som miljövariabler i compose:

| Nyckel | Betydelse |
|---|---|
| `McServer__Host` | Värden RCON lyssnar på. Containernamnet om båda ligger på samma nätverk. |
| `McServer__RconPort` | Samma som `rcon.port`. |
| `McServer__RconPassword` | Samma som `rcon.password`. Tomt = placeholder-läge. |
| `McServer__ContainerName` | Minecraft-containern. Tomt = start och omstart avstängda. |
| `McServer__DockerSocket` | Standard `/var/run/docker.sock`. |
| `McServer__LogPath` | Sökväg till `latest.log` inuti den här containern. |
| `McServer__StopTimeoutSeconds` | Hur länge appen väntar på att servern ska avsluta. |

I server.properties måste `enable-rcon=true`, `rcon.password` och `rcon.port` vara satta,
och containern måste startas om innan RCON börjar lyssna.

### Rättigheter

Appen kör som uid 1654 och behöver två grupper i `group_add`:

* **Minecraft-gruppen** — för att skriva `server.properties` och läsa loggen. Serverns
  datakatalog är typiskt `750`, så utan gruppen kommer appen inte ens in i katalogen och
  loggpanelen står tom.
* **Docker-gruppen** — för att nå socketen. `stat -c '%g' /var/run/docker.sock`.

### Om Docker-socketen

Att montera `/var/run/docker.sock` ger appen full kontroll över Docker på värden, vilket i
praktiken är root. `:ro` hjälper inte: flaggan gör bara själva socket-filen skrivskyddad,
inte API:t bakom den — en läsmonterad socket kan fortfarande starta och stoppa containrar.
Appen använder tre anrop, alla mot det konfigurerade containernamnet: inspect, start och
stop. Vill du inte ge den den rättigheten, lämna `McServer__ContainerName` tomt; då
fungerar kommandon och stopp via RCON, medan start och omstart svarar med en förklaring.

### Vad RCON inte kan

* **Ping per spelare** finns inte i protokollet. Kolumnen visar `—`.
* **Sessionslängd** räknas från när appen först såg spelaren online, inte från inloggningen.
* **Start** går inte via RCON alls, därav Docker-socketen.

### Jenkins

Deploy-steget hämtar RCON-lösenordet från credential-id `mcservermgmnt-rcon-password` och
skickar in det som `MC_RCON_PASSWORD`, som compose-filen läser. Kör du compose för hand
behöver du sätta variabeln själv, eller lägga den i en `.env` bredvid compose-filen.

### Gemensamt nätverk

`McServer__Host=mcserver` fungerar bara om båda containrarna ligger på samma
användardefinierade nätverk — Dockers inbyggda DNS finns inte på default-bryggan. Skapa
ett nätverk som ingen av stackarna äger:

```sh
docker network create mcnet
```

Lägg sedan in det i Minecraft-serverns compose-fil:

```yaml
services:
  minecraft:
    # ... oförändrat ...
    networks:
      - mcnet

networks:
  mcnet:
    external: true
```

Både `mcserver` (container_name) och `minecraft` (tjänstenamnet) går att slå upp från den
här appen när de ligger på nätverket. Publicerade portar fortsätter fungera som förut.

Med nätverket på plats kan `- "25575:25575"` tas bort ur Minecraft-serverns portlista.
RCON skickar lösenordet i klartext, så det bör inte ligga öppet mot internet; appen når
porten inifrån nätverket ändå. `25565` och `8123` måste ligga kvar — de ska nås utifrån.

Ordningen spelar roll: nätverket måste finnas innan någon av stackarna startar, annars
vägrar compose med `network mcnet declared as external, but could not be found`.

