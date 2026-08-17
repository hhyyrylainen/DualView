const DEFAULT_SETTINGS = {
  serverUrl: "",
  accessKey: "",
};

const READY_MESSAGE = "DVREADY";
const GREETING_MESSAGE = "HELODV3";
const RECONNECT_ALARM = "dualview-web-reconnect";
const KEEP_ALIVE_INTERVAL_MS = 60000;

let socket;
let status = "disconnected";
let statusDetail = "Not connected";
let reconnectRequested = false;
let pendingTabIds = new Set();

browser.runtime.onInstalled.addListener(async () => {
  await browser.storage.local.set(await getSettings());
  await browser.alarms.create(RECONNECT_ALARM, { periodInMinutes: 1 });
  createContextMenus();
  await updateActionIcon();
});

browser.runtime.onStartup.addListener(async () => {
  createContextMenus();
  await browser.alarms.create(RECONNECT_ALARM, { periodInMinutes: 1 });
  await connectIfConfigured();
});

browser.alarms.onAlarm.addListener(async alarm => {
  if (alarm.name !== RECONNECT_ALARM) {
    return;
  }

  if (status === "connected") {
    sendPing();
  } else {
    await connectIfConfigured();
  }
});

browser.contextMenus.onClicked.addListener(async (info, tab) => {
  if (!tab?.id) {
    return;
  }

  switch (info.menuItemId) {
    case "send-page":
      await sendContextMessage(tab.id, {
        type: "sendPage",
        pageUrl: info.pageUrl,
        title: tab.title ?? "",
      });
      break;
    case "send-image":
      await sendContextMessage(tab.id, {
        type: "sendImage",
        imageUrl: info.srcUrl,
        pageUrl: info.pageUrl,
      });
      break;
    case "scan-link":
      await sendContextMessage(tab.id, {
        type: "scanLink",
        linkUrl: info.linkUrl,
        pageUrl: info.pageUrl,
      });
      break;
  }
});

browser.runtime.onMessage.addListener(async message => {
  if (message.type === "get-status") {
    return getPublicStatus();
  }

  if (message.type === "save-settings") {
    await browser.storage.local.set({
      serverUrl: message.serverUrl.trim(),
      accessKey: message.accessKey.trim(),
    });
    await disconnect("Settings changed");
    await connectIfConfigured();
    return getPublicStatus();
  }

  if (message.type === "connect") {
    await connectIfConfigured(true);
    return getPublicStatus();
  }

  if (message.type === "disconnect") {
    await disconnect("Disconnected by user");
    return getPublicStatus();
  }

  return undefined;
});

function createContextMenus() {
  browser.contextMenus.removeAll().then(() => {
    browser.contextMenus.create({
      id: "send-page",
      title: "Send to DualView",
      contexts: ["page"],
    });
    browser.contextMenus.create({
      id: "send-image",
      title: "Send Image to DualView",
      contexts: ["image"],
    });
    browser.contextMenus.create({
      id: "scan-link",
      title: "Send Link to scan in DualView",
      contexts: ["link"],
    });
  });
}

async function getSettings() {
  const settings = await browser.storage.local.get(DEFAULT_SETTINGS);
  return {
    serverUrl: settings.serverUrl ?? "",
    accessKey: settings.accessKey ?? "",
  };
}

async function connectIfConfigured(forceReconnect = false) {
  const settings = await getSettings();
  if (!settings.serverUrl || !settings.accessKey) {
    setStatus("disconnected", "Enter a server URL and access key");
    return;
  }

  if (status === "connected" || status === "connecting") {
    if (!forceReconnect) {
      return;
    }

    await disconnect("Reconnecting");
  }

  let socketUrl;
  try {
    socketUrl = createSocketUrl(settings.serverUrl);
  } catch (error) {
    setStatus("error", error.message);
    return;
  }

  reconnectRequested = true;
  setStatus("connecting", "Connecting to DualView");

  try {
    socket = new WebSocket(socketUrl);
    socket.binaryType = "arraybuffer";
    socket.addEventListener("open", () => {
      socket.send(GREETING_MESSAGE);
      socket.send(settings.accessKey);
    });
    socket.addEventListener("message", event => handleSocketMessage(event));
    socket.addEventListener("close", event => handleSocketClosed(event));
    socket.addEventListener("error", () => {
      setStatus("error", "Unable to connect to DualView");
    });
  } catch (error) {
    setStatus("error", `Unable to open websocket: ${error.message}`);
  }
}

function createSocketUrl(serverUrl) {
  const parsedUrl = new URL(serverUrl.includes("://") ? serverUrl : `http://${serverUrl}`);
  if (parsedUrl.protocol === "http:") {
    parsedUrl.protocol = "ws:";
  } else if (parsedUrl.protocol === "https:") {
    parsedUrl.protocol = "wss:";
  }

  if (parsedUrl.protocol !== "ws:" && parsedUrl.protocol !== "wss:") {
    throw new Error("The server URL must use http, https, ws, or wss");
  }

  parsedUrl.pathname = "/api/v3/browser-plugin";
  parsedUrl.search = "";
  parsedUrl.hash = "";
  return parsedUrl.toString();
}

function handleSocketMessage(event) {
  if (typeof event.data === "string") {
    if (event.data === READY_MESSAGE) {
      reconnectRequested = true;
      setStatus("connected", "Connected to DualView");
      sendPing();
    }
    return;
  }

  const message = decodeProtocolMessage(event.data);
  if (message?.type === "pong") {
    setStatus("connected", "Connected to DualView");
  }
}

function handleSocketClosed(event) {
  socket = undefined;
  const wasConnected = status === "connected";
  const reason = event.reason || (event.code === 1000 ? "Connection closed" : "Server rejected or closed the connection");
  setStatus("disconnected", reason);

  if (pendingTabIds.size > 0) {
    for (const tabId of pendingTabIds) {
      showTabToast(tabId, "DualView rejected the request.", "error");
    }
    pendingTabIds.clear();
  } else if (wasConnected && event.code !== 1000) {
    console.warn("DualView websocket closed:", event.code, reason);
  }
}

async function disconnect(reason) {
  reconnectRequested = false;
  if (socket && socket.readyState === WebSocket.OPEN) {
    socket.close(1000, reason);
  }
  socket = undefined;
  setStatus("disconnected", reason);
}

function sendPing() {
  if (status === "connected") {
    sendProtocolMessage({ type: "ping" });
  }
}

async function sendContextMessage(tabId, message) {
  if (status !== "connected" || !socket || socket.readyState !== WebSocket.OPEN) {
    await showTabToast(tabId, "DualView is not connected. Open the DualView Web toolbar menu to connect.", "error");
    await connectIfConfigured();
    return;
  }

  pendingTabIds.add(tabId);
  sendProtocolMessage({ ...message, sentAt: new Date().toISOString() });
}

function sendProtocolMessage(message) {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    return false;
  }

  const messageBytes = new TextEncoder().encode(JSON.stringify(message));
  const frame = new Uint8Array(4 + messageBytes.length);
  new DataView(frame.buffer).setUint32(0, messageBytes.length, false);
  frame.set(messageBytes, 4);
  socket.send(frame);
  return true;
}

function decodeProtocolMessage(data) {
  const frame = new Uint8Array(data);
  if (frame.length < 4) {
    console.warn("DualView sent a frame without a length header");
    return undefined;
  }

  const messageLength = new DataView(frame.buffer, frame.byteOffset, frame.byteLength).getUint32(0, false);
  if (messageLength !== frame.length - 4) {
    console.warn("DualView sent a frame with an invalid length");
    return undefined;
  }

  try {
    return JSON.parse(new TextDecoder().decode(frame.subarray(4)));
  } catch (error) {
    console.warn("DualView sent invalid JSON", error);
    return undefined;
  }
}

async function setStatus(newStatus, detail) {
  status = newStatus;
  statusDetail = detail;
  await updateActionIcon();
}

async function updateActionIcon() {
  const iconPath = status === "connected" ? "icons/dualview.svg" : "icons/dualview-disconnected.svg";
  await browser.action.setIcon({
    path: {
      16: iconPath,
      32: iconPath,
      48: iconPath,
      96: iconPath,
    },
  });
  await browser.action.setTitle({ title: `DualView Web: ${statusDetail}` });
}

function getPublicStatus() {
  return {
    status,
    detail: statusDetail,
  };
}

async function showTabToast(tabId, message, level) {
  try {
    await browser.tabs.sendMessage(tabId, { type: "show-toast", message, level });
  } catch (error) {
    console.debug("Unable to show a DualView toast in the tab", error);
  }
}

connectIfConfigured();
