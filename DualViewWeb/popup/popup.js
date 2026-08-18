const serverUrlInput = document.getElementById("server-url");
const accessKeyInput = document.getElementById("access-key");
const sendCookiesInput = document.getElementById("send-cookies");
const statusElement = document.getElementById("connection-status");
const detailElement = document.getElementById("connection-detail");

document.querySelectorAll(".tab-button").forEach(button => {
  button.addEventListener("click", () => selectTab(button.dataset.tab));
});

document.getElementById("show-access-key").addEventListener("change", event => {
  accessKeyInput.type = event.target.checked ? "text" : "password";
});

document.getElementById("save-connect").addEventListener("click", async () => {
  const status = await browser.runtime.sendMessage({
    type: "save-settings",
    serverUrl: serverUrlInput.value,
    accessKey: accessKeyInput.value,
    sendCookies: sendCookiesInput.checked,
  });
  updateStatus(status);
  selectTab("info");
});

document.getElementById("disconnect").addEventListener("click", async () => {
  updateStatus(await browser.runtime.sendMessage({ type: "disconnect" }));
});

async function initialize() {
  const settings = await browser.storage.local.get({ serverUrl: "", accessKey: "", sendCookies: false });
  serverUrlInput.value = settings.serverUrl;
  accessKeyInput.value = settings.accessKey;
  sendCookiesInput.checked = settings.sendCookies === true;
  updateStatus(await browser.runtime.sendMessage({ type: "get-status" }));
  window.setInterval(refreshStatus, 1000);
}

async function refreshStatus() {
  updateStatus(await browser.runtime.sendMessage({ type: "get-status" }));
}

function selectTab(tabName) {
  document.querySelectorAll(".tab-button").forEach(button => {
    button.classList.toggle("selected", button.dataset.tab === tabName);
  });
  document.querySelectorAll(".tab-panel").forEach(panel => {
    panel.classList.toggle("selected", panel.id === tabName);
  });
}

function updateStatus(status) {
  statusElement.textContent = status.detail;
  statusElement.className = `status ${status.status}`;
  detailElement.textContent = status.detail;
}

initialize();
