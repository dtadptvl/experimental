(() => {
  const term = new Terminal({
    cursorBlink: true,
    scrollback: 20000,
    fontFamily: "Cascadia Mono, Consolas, monospace",
    fontSize: 14,
    theme: { background: "#0c0c0c" }
  });
  const fitAddon = new FitAddon.FitAddon();
  term.loadAddon(fitAddon);
  term.open(document.getElementById("terminal"));

  const encoder = new TextEncoder();
  let decoder = new TextDecoder("utf-8");

  function bytesToBase64(bytes) {
    let binary = "";
    const chunk = 0x8000;
    for (let i = 0; i < bytes.length; i += chunk) {
      binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
    }
    return btoa(binary);
  }

  function base64ToBytes(base64) {
    const binary = atob(base64 || "");
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
  }

  function sendResize() {
    window.chrome.webview.postMessage({ type: "resize", cols: term.cols, rows: term.rows });
  }

  term.onData(data => {
    window.chrome.webview.postMessage({ type: "input", data: bytesToBase64(encoder.encode(data)) });
  });

  term.onResize(sendResize);

  window.chrome.webview.addEventListener("message", event => {
    const msg = event.data || {};
    if (msg.type === "reset") {
      term.reset();
      term.clear();
      decoder = new TextDecoder("utf-8");
      return;
    }
    if (msg.type === "output" && msg.data) {
      const bytes = base64ToBytes(msg.data);
      term.write(decoder.decode(bytes, { stream: true }));
    }
  });

  let resizeTimer;
  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      fitAddon.fit();
      sendResize();
    }, 50);
  });

  setTimeout(() => {
    fitAddon.fit();
    sendResize();
    term.focus();
  }, 0);
})();
