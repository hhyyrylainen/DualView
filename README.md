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
