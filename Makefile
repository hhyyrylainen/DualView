.DEFAULT_GOAL := help

DOTNET ?= dotnet
SYSTEMCTL ?= systemctl
CONFIGURATION ?= Release
# The current ImageMagick dependency is x64-specific.
# TODO: other platforms
RUNTIME ?= linux-x64
PUBLISH_DIR := $(CURDIR)/bin/publish/$(RUNTIME)/$(CONFIGURATION)
GUI_PUBLISH_DIR := $(PUBLISH_DIR)/gui
SERVER_PUBLISH_DIR := $(PUBLISH_DIR)/server
INSTALL_DIR := $(HOME)/bin
SERVER_INSTALL_DIR := $(INSTALL_DIR)/DualViewServer
SYSTEMD_USER_DIR := $(if $(XDG_CONFIG_HOME),$(XDG_CONFIG_HOME),$(HOME)/.config)/systemd/user
SERVICE := dualview-server.service

.PHONY: help publish publish-server publish-gui install install-files install-service start stop restart enable disable status

help:
	@printf '%s\n' \
	  'make install         Publish and install both apps, update the service, enable and start it' \
	  'make publish         Publish both apps without installing' \
	  'make publish-server  Publish the server only' \
	  'make publish-gui     Publish the GUI only' \
	  'make install-files   Publish and install both apps and service, leaving the server stopped' \
	  'make install-service Install/update the user service and reload systemd' \
	  'make start / stop    Start or stop the server user service' \
	  'make restart         Restart the server user service' \
	  'make enable / disable Enable or disable automatic startup of the service' \
	  'make status          Show server service status'

# Publish sequentially because the projects share build outputs in Backend and Shared.
publish: publish-server
	$(MAKE) publish-gui

publish-server:
	$(DOTNET) publish DualView.Server/DualView.Server.csproj \
	  --configuration "$(CONFIGURATION)" --runtime "$(RUNTIME)" --self-contained true \
	  --output "$(SERVER_PUBLISH_DIR)"

publish-gui:
	$(DOTNET) publish DualView.GUI/DualView.GUI.csproj \
	  --configuration "$(CONFIGURATION)" --runtime "$(RUNTIME)" --self-contained true \
	  -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true \
	  -p:PublishReadyToRun=true \
	  -p:DebugType=None -p:DebugSymbols=false --output "$(GUI_PUBLISH_DIR)"

install: install-files
	$(MAKE) enable
	$(MAKE) start

install-files: publish
	$(MAKE) stop
	install -d "$(INSTALL_DIR)" "$(SERVER_INSTALL_DIR)"
	install -m 755 "$(GUI_PUBLISH_DIR)/DualView.GUI" "$(INSTALL_DIR)/DualView.GUI"
	cp -a "$(SERVER_PUBLISH_DIR)/." "$(SERVER_INSTALL_DIR)/"
	$(MAKE) install-service

install-service:
	install -d "$(SYSTEMD_USER_DIR)"
	install -m 644 packaging/dualview-server.service "$(SYSTEMD_USER_DIR)/$(SERVICE)"
	$(SYSTEMCTL) --user daemon-reload

start:
	$(SYSTEMCTL) --user start $(SERVICE)

restart:
	$(SYSTEMCTL) --user restart $(SERVICE)

stop:
	@# On a first installation there is no unit to stop. Other failures must abort installation.
	@if $(SYSTEMCTL) --user cat $(SERVICE) >/dev/null 2>&1; then \
	  $(SYSTEMCTL) --user stop $(SERVICE); \
	else \
	  $(SYSTEMCTL) --user show --property=LoadState --value $(SERVICE) | \
	    grep -qx 'not-found'; \
	fi

enable:
	$(SYSTEMCTL) --user enable $(SERVICE)

disable:
	$(SYSTEMCTL) --user disable $(SERVICE)

status:
	$(SYSTEMCTL) --user status $(SERVICE)
