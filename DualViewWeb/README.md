# DualView Web

Firefox Manifest V3 extension for sending browser content to DualView.

## Load for development

1. Open `about:debugging#/runtime/this-firefox` in Firefox.
2. Select **Load Temporary Add-on**.
3. Select this folder's `manifest.json`.
4. Open the toolbar popup and set the DualView server URL and browser plugin access key.

The extension connects to `/api/v3/browser-plugin`. Page context menus are available on pages, tabs, and multiple selected tabs. The tab menu sends the clicked tab, while **Send Tab Group to DualView** sends selected tabs in tab-index order and waits for each matching server acknowledgment before continuing. A failed or timed-out request stops the group.

The server acknowledges queued `sendImage` requests with the request ID supplied by the extension.
