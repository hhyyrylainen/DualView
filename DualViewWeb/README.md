# DualView Web

Firefox Manifest V3 extension for sending browser content to DualView.

## Load for development

1. Open `about:debugging#/runtime/this-firefox` in Firefox.
2. Select **Load Temporary Add-on**.
3. Select this folder's `manifest.json`.
4. Open the toolbar popup and set the DualView server URL and browser plugin access key.

The extension connects to `/api/v3/browser-plugin`. Its context-menu message types are currently placeholders: `sendPage`, `sendImage`, and `scanLink`. The server currently supports only `ping`, so it will reject these commands and the extension will show a short error toast on the clicked tab until matching server handlers are added.
