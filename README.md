# McAdmin [![Build Status](https://jenkins.jonteohr.xyz/buildStatus/icon?job=MC_MGMNT)](https://jenkins.jonteohr.xyz/view/Web/job/MC_MGMNT/)

A web console for a Minecraft server. Start and stop it, watch what it is saying, edit
`server.properties` and the whitelist without opening a text editor, and hand out logins
to the people who help you run it — without handing out SSH.

It is a single ASP.NET Core app (Blazor Server, .NET 10) that sits next to the Minecraft
server on the same machine and talks to it three ways, because no single one of them is
enough:

| For | It uses | Why not something else |
|-----|---------|------------------------|
| Commands, player list, stopping | RCON | The protocol the server already speaks. |
| Starting and restarting | The Docker socket | RCON can stop a server but never start one — once it is down there is no listener left. |
| The console output | `latest.log`, mounted read-only | RCON only ever returns the answer to a command, never the server's own output. |

Nothing is required for it to boot. With no RCON password configured the app starts
anyway, on placeholder data, and every page says so instead of failing to connect on each
render — so a fresh checkout runs and can be looked at before any of it is pointed at a
real server.

---

## What is in it

**Console** (`/`) — server state, uptime, version and player count; start, stop and
restart; a command box that goes straight to RCON; and the tail of the server log,
levelled and timestamped.

**Server settings** (`/settings`) — `server.properties` as a proper form. Every key the
vanilla server understands, grouped into World, Players, Gameplay and the rest, each with
the explanation from the Minecraft wiki and the right control for its type. Settings the
server only reads at startup are tagged as such. Nothing is written until you save.

**Whitelist** (`/whitelist`) — `whitelist.json`, with names resolved against Mojang so a
username is enough to add someone.

**Accounts** (`/users`, administrators only) — who can sign in. Two roles: an
administrator manages accounts, a user manages the server. Passwords are PBKDF2, sessions
are cookies with a twelve-hour sliding expiry.

**Addons** — the console extends itself. A `.dll` dropped into the addons folder can add
pages to the sidebar and edit the config files of the Minecraft plugins installed on the
server. See [`API/src/README.md`](API/src/README.md).

---

## Layout

There is no solution file. Two projects, one referencing the other:

| Path | What it is |
|------|------------|
| `Web/src` | `McServerMgmnt` — the web app. Pages in `Components/`, everything else in `Services/`. |
| `API/src` | `McAdminPlugins` — the contract addon authors build against, shipped as a loose `.dll`. |
| `Dockerfile` | Builds both. The context is the repository root, because the web project references the API project. |
| `docker-compose.yml` | The deployment: ports, volumes, and the two groups the container needs. |
| `Jenkinsfile` | Build, publish the addon API as an artifact, deploy, health check. |

`Services/` is worth knowing the shape of:

```
Services/
  Rcon/            RconClient, the Docker Engine calls, the log tail reader
  Factories/       server.properties and whitelist.json, read and written on disk
  Plugins/         the addon loader, the registry, and the sandboxed file access
  UserService.cs   accounts, in SQLite through EF Core
```

---

## Running it

### Against a real server, with Compose

The compose file assumes the Minecraft server is a container of its own, on a shared
network, with its data under `/opt/minecraft`. Adjust the paths to match your host.

```sh
docker network create mcnet          # once; owned by neither stack
export MC_RCON_PASSWORD=...          # same as rcon.password in server.properties
docker compose up -d --build
```

Then open `http://<host>:5700` and sign in as **admin** / **admin**. The first
administrator is seeded on an empty database and flagged, so the UI nags until the
password is changed. Do that before anything else.

Three things about the compose file are load-bearing and easy to get wrong:

- **Keep `docker-compose.yml` in the repository root.** Compose derives the project name
  from the directory the file sits in, and that name prefixes the `mcservermgmnt_data`
  and `_keys` volumes. Move the file into a subfolder and the deploy comes up with a new,
  empty database and a new set of signing keys.
- **Keep the two named volumes.** `_data` is the account database. `_keys` is the
  DataProtection folder that signs auth cookies and antiforgery tokens; without it every
  redeploy logs everyone out and Blazor circuits fail to reconnect.
- **Do not set `user:`.** `/app/data` and the DataProtection directory are owned by uid
  1654 inside the image. A different uid loses both.

The container runs as a non-root user and needs two supplementary groups: the Minecraft
server's, to write `server.properties`, and Docker's, to reach the socket. Find them on
the host and put them in `group_add`:

```sh
stat -c '%g' /opt/minecraft/server.properties   # and: sudo chmod g+w on the file
stat -c '%g' /var/run/docker.sock
```

### From the source tree

```sh
dotnet run --project Web/src
```

`http://localhost:6234`, or `https://localhost:7227` with the `https` profile. It boots
with no Minecraft server behind it: RCON stays unconfigured, the placeholder controller
takes over, and the pages say so.

Two folders in `Web/src` exist for this and are ignored by both git and the Docker
context — in the container they are a volume and a bind mount instead:

- `Web/src/addons/` — addon assemblies to load on start.
- `Web/src/plugins/` — stands in for the Minecraft server's plugins folder, so addons
  that edit plugin config have something to edit.

Point the app at real files by dropping a `server.properties` and a `whitelist.json`
beside the project, or by setting the configuration below.

---

## Configuration

`appsettings.json`, or environment variables using `__` for the nesting — which is what
compose does.

### `McServer`

| Key | Default | Meaning |
|-----|---------|---------|
| `Host` | `minecraft` | Where the RCON listener is. Under compose, the Minecraft container's name. |
| `RconPort` | `25575` | Matches `rcon.port`. |
| `RconPassword` | *(empty)* | Matches `rcon.password`. **Empty leaves the placeholder controller in place** — this is the switch that decides whether anything is real. |
| `Address` | *(none)* | Shown on the console page. Falls back to `Host` plus `server-port`. |
| `ContainerName` | *(empty)* | The Minecraft container. Without it, start and restart are unavailable — stop still works, over RCON. |
| `DockerSocket` | `/var/run/docker.sock` | |
| `LogPath` | *(empty)* | The server's `latest.log`, mounted read-only. Empty leaves the log panel empty. |
| `StopTimeoutSeconds` | `90` | How long to wait for a clean shutdown after `stop`. |
| `CommandTimeoutSeconds` | `10` | Per-command RCON timeout, connect included. |
| `LogTailLines` | `200` | How much of the log the console panel reads at a time. |

### `Plugins`

| Key | Default | Meaning |
|-----|---------|---------|
| `Enabled` | `true` | `false` boots without looking in the drop-in folder at all. |
| `Path` | `addons` | Where addon assemblies are loaded from. |
| `ServerPluginsPath` | `plugins` | The Minecraft server's own plugins folder — the one addons are allowed to edit. |

These two are **not** the same folder, and the names invite the mistake. `addons` holds
extensions to McAdmin; `plugins` belongs to the Minecraft server. Both are relative to
the working directory (`/app` in the container) unless given as absolute paths.

### `ConnectionStrings:AccountsDb`

The SQLite file holding accounts. `Data Source=/app/data/mcservermgmnt.db` under compose,
so it lands on the volume.

---

## Deploying

`Jenkinsfile` drives it, on a node with Docker and no .NET SDK needed:

1. **Build the image** from the repository root.
2. **Export the addon API.** `McAdminPlugins.dll` is pulled out of the publish output with
   `--target api --output` and archived, so addon authors download the exact file the
   running app loads — the same build, therefore the same contract. The layers are
   already cached from the previous stage.
3. **Deploy** with `docker compose up -d --force-recreate`, with the RCON password coming
   from Jenkins credentials.
4. **Health check** `/login`, five attempts.
5. **Prune** dangling layers.

To pull the addon contract out by hand:

```sh
DOCKER_BUILDKIT=1 docker build -f Dockerfile --target api --output type=local,dest=artifacts .
```

---

## Writing an addon

An addon is an ordinary .NET assembly built against `McAdminPlugins`. It can describe
pages that the host renders in its own design, bring its own Razor components when a
description will not stretch that far, and read and write the config files of the
Minecraft plugins on the server — through a YAML parser that puts the file back the way
it found it, comments and all.

```csharp
public sealed class MyAddon(IPluginPages pages, IServerPluginFiles files) : IPlugin
{
    public Task Load()
    {
        pages.AddPage(new PluginPage("essentials", "Essentials") { /* sections */ });

        return Task.CompletedTask;
    }
}
```

Build it, drop the output into the addons folder, restart. The full walkthrough — the
section types, the YAML API, and how loading works — is in
[`API/src/README.md`](API/src/README.md). There is a worked example at
[McAdminApp/Plugin-Example](https://github.com/McAdminApp/Plugin-Example).

Addons run in the app's process with the app's privileges. The only thing standing
between one and the rest of the filesystem is `IServerPluginFiles`, which refuses to
leave the plugins folder. Install code you trust.

---

## License

GPL-3.0. See [LICENSE](LICENSE).
