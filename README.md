DualView
========

An image manager and viewer. Implements 2 interfaces: GUI an Avalonia
desktop app, and a web app, that both connect to the DualView server.


This is the 3rd version of DualView written in C#.

## Linux installation

With the .NET 10 SDK, GNU Make, and a running systemd user session installed, run
`make install` as your normal user (without sudo). This uses `dotnet publish` to
create self-contained Release executables for Linux x64. The GUI uses ReadyToRun
compilation to improve startup time; the server uses JIT compilation. The GUI is bundled into
`~/bin/DualView.GUI`, and the complete server output (including the web client)
is installed into `~/bin/DualViewServer`.

Installation stops an existing server before replacing files, updates
`dualview-server.service` in `${XDG_CONFIG_HOME:-~/.config}/systemd/user`, reloads
systemd, and enables and starts the service. Application data and configuration
remain in their usual user data directories. Add `~/bin` to your `PATH` to run
`DualView.GUI` by name. Self-contained publishing includes .NET; the applications
still require their native system dependencies.

Available operations:

- `make publish`, `make publish-server`, `make publish-gui`: publish without installing.
- `make install-files`: publish and install files and the service, leaving the server stopped.
- `make install-service`: update just the service definition and reload systemd.
- `make start`, `make stop`, `make restart`: control the server now.
- `make enable`, `make disable`: control automatic startup independently of its current state.
- `make status`: show the server status.

Publish output is stored under `bin/publish`. Build settings can be overridden,
for example `make publish CONFIGURATION=Debug`. `RUNTIME` defaults to `linux-x64`,
matching the current x64 ImageMagick dependency.

The enabled service starts with your systemd user session. To run it at boot
without logging in, enable lingering separately with `loginctl enable-linger "$USER"`
(your system may require administrator authorization). View server output with
`journalctl --user -u dualview-server.service`.

## Automatically show browser imports

Accepted browser plugin image, page, and link requests ask connected desktop clients
to open or activate the importer. To also start the GUI when no client responds,
add an executable path to `server-config.yaml` in the server's data directory:

```yaml
GUILauncher: /home/your-user/bin/DualView.GUI
```

The value is an executable path (or an executable wrapper script), without command
line arguments. The server must run in an environment that can access your desktop
session. On Linux, the GUI starts in a separate group when `/usr/bin/setsid` is
available. Leaving the setting unset disables launching; existing clients still
receive activation requests.
