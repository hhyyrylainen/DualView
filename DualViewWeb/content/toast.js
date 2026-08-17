browser.runtime.onMessage.addListener(message => {
  if (message.type !== "show-toast") {
    return;
  }

  const existingToast = document.getElementById("dualview-web-toast");
  existingToast?.remove();

  const toast = document.createElement("div");
  toast.id = "dualview-web-toast";
  toast.textContent = message.message;
  toast.setAttribute("role", "alert");
  toast.style.cssText = [
    "position:fixed",
    "right:20px",
    "bottom:20px",
    "z-index:2147483647",
    "max-width:360px",
    "padding:12px 16px",
    "border-radius:8px",
    "box-shadow:0 4px 18px rgba(0, 0, 0, .35)",
    "font:14px/1.4 system-ui, sans-serif",
    "color:#fff",
    `background:${message.level === "error" ? "#b3261e" : "#1769aa"}`,
  ].join(";");
  document.documentElement.append(toast);
  window.setTimeout(() => toast.remove(), 5000);
});
