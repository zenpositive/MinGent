let settings = {};
let origin = "https://gemini.google.com";
let app = origin + "/app";

let socket = null;
let open = false;
let beat = 0;
let tab = 0;
let mine = 0;
let port = null;
let portTab = 0;
let jobs = {};
let mismatch = false;
let reading = false;
let trusted = false;
let nonce = "";
let retry = 0;

const sleep = ms => new Promise(done => setTimeout(done, ms));

const hex = buffer => Array.from(new Uint8Array(buffer)).map(b => b.toString(16).padStart(2, "0")).join("");

const sign = async text => {
  const coder = new TextEncoder();
  const key = await crypto.subtle.importKey("raw", coder.encode(settings.token || ""), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return hex(await crypto.subtle.sign("HMAC", key, coder.encode(text)));
};

const send = message => {
  if (!socket || !open) return;
  try { socket.send(JSON.stringify(message)); } catch (e) {}
};

const dropPort = why => {
  const old = port;
  port = null;
  portTab = 0;
  const lost = jobs;
  jobs = {};
  for (const id in lost) send({ type: "chunk", id: id, done: true, err: why });
  if (old) { try { old.disconnect(); } catch (e) {} }
};

const load = async () => {
  try {
    const answer = await fetch(chrome.runtime.getURL("config.json"), { cache: "no-store" });
    const read = await answer.json();
    if (!read || !read.port || !read.token) return;
    settings = read;
    origin = read.origin || "https://gemini.google.com";
    app = origin + "/app";
  } catch (e) {}
};

const connect = async () => {
  if (socket || reading) return;
  reading = true;
  try { await load(); } finally { reading = false; }
  if (socket || !settings.port) return;
  mismatch = false;
  let next;
  try { next = new WebSocket("ws://127.0.0.1:" + settings.port + "/helper"); } catch (e) { return; }
  socket = next;
  next.onopen = () => {
    open = true;
    trusted = false;
    nonce = hex(crypto.getRandomValues(new Uint8Array(16)));
    send({ type: "hello", nonce: nonce, version: chrome.runtime.getManifest().version, agent: navigator.userAgent });
    clearInterval(beat);
    beat = setInterval(() => { if (open) send({ type: "ping" }); }, 20000);
  };
  next.onmessage = event => {
    let message;
    try { message = JSON.parse(event.data); } catch (e) { return; }
    handle(message).catch(e => {
      if (message && message.id) send({ type: "reply", id: message.id, ok: false, why: String((e && e.message) || e) });
    });
  };
  const closed = () => {
    if (socket !== next) return;
    socket = null;
    open = false;
    trusted = false;
    clearInterval(beat);
    dropPort("MinGent closed the connection");
    if (!mismatch) again();
  };
  next.onclose = closed;
  next.onerror = closed;
};

const usable = one => one && !one.discarded && one.url && one.url.startsWith(origin + "/");

const findTab = async create => {
  let known = null;
  if (tab) {
    try { known = await chrome.tabs.get(tab); } catch (e) { known = null; }
    if (!known) {
      if (mine === tab) mine = 0;
      tab = 0;
    }
  }
  if (usable(known)) return known;
  const found = await chrome.tabs.query({ url: origin + "/*" });
  let best = null;
  for (const one of found) {
    if (one.discarded) continue;
    if (!best || (one.status === "complete" && best.status !== "complete")) best = one;
  }
  if (!best && mine) {
    try { best = await chrome.tabs.get(mine); } catch (e) { mine = 0; }
  }
  if (!best && create) {
    let windows = [];
    try { windows = await chrome.windows.getAll({ windowTypes: ["normal"] }); } catch (e) {}
    if (windows.length > 0) {
      try { best = await chrome.tabs.create({ url: app, active: false, pinned: true }); } catch (e) { best = null; }
    }
    if (!best) {
      let made;
      try { made = await chrome.windows.create({ url: app, state: "minimized" }); }
      catch (e) { made = await chrome.windows.create({ url: app, focused: false }); }
      best = made.tabs[0];
      try { await chrome.tabs.update(best.id, { pinned: true }); } catch (e) {}
    }
    mine = best.id;
  }
  if (!best) return null;
  tab = best.id;
  try { await chrome.tabs.update(tab, { autoDiscardable: false }); } catch (e) {}
  return best;
};

const loaded = async id => {
  for (let i = 0; i < 160; i++) {
    const now = await chrome.tabs.get(id);
    if (now.status === "complete") return now;
    await sleep(250);
  }
  throw new Error("The Gemini tab did not finish loading");
};

const pageFacts = async id => {
  const got = await chrome.scripting.executeScript({
    target: { tabId: id },
    world: "MAIN",
    func: () => {
      const page = window.WIZ_global_data || {};
      return {
        host: location.hostname,
        path: location.pathname,
        at: page.SNlM0e || "",
        build: page.cfb2h || "",
        sid: page.FdrFJe || "",
        push: page.qKIAYe || ""
      };
    }
  });
  return got && got[0] ? got[0].result : null;
};

const session = async message => {
  let found = await findTab(message.create !== false);
  if (!found) return { ok: false, why: "notab" };
  if (message.reload) {
    dropPort("The Gemini tab was reloaded");
    await chrome.tabs.reload(found.id);
    await sleep(300);
  }
  found = await loaded(found.id);
  let facts = null;
  for (let i = 0; i < 40 && !(facts && facts.build); i++) {
    if (i > 0) await sleep(250);
    try { facts = await pageFacts(found.id); } catch (e) { facts = null; break; }
  }
  if (!facts || !facts.build) return { ok: false, why: "page" };
  facts.ok = true;
  return facts;
};

const link = async id => {
  if (port && portTab === id) return port;
  dropPort("The Gemini tab changed");
  await chrome.scripting.executeScript({ target: { tabId: id }, files: ["content.js"] });
  const made = chrome.tabs.connect(id, { name: "mingent" });
  made.onMessage.addListener(message => {
    if (message && message.done) delete jobs[message.id];
    send(message);
  });
  made.onDisconnect.addListener(() => {
    if (port === made) dropPort("The Gemini tab was closed or reloaded");
  });
  port = made;
  portTab = id;
  return made;
};

const start = async message => {
  try {
    const found = await findTab(true);
    if (!found) throw new Error("No Gemini tab could be opened");
    await loaded(found.id);
    const line = await link(found.id);
    jobs[message.id] = true;
    line.postMessage(message);
  } catch (e) {
    delete jobs[message.id];
    send({ type: "chunk", id: message.id, done: true, err: String((e && e.message) || e) });
  }
};

const handle = async message => {
  if (message.type === "welcome") {
    if (message.proof !== await sign("server:" + nonce)) {
      mismatch = true;
      try { socket.close(); } catch (e) {}
      return;
    }
    trusted = true;
    send({ type: "proof", proof: await sign("helper:" + message.nonce) });
    return;
  }
  if (!trusted) return;
  if (message.type === "reload") {
    chrome.runtime.reload();
    return;
  }
  if (message.type === "session") {
    const answer = await session(message);
    answer.type = "reply";
    answer.id = message.id;
    send(answer);
    return;
  }
  if (message.type === "start") { await start(message); return; }
  if (message.type === "end") {
    delete jobs[message.id];
    if (port) { try { port.postMessage(message); } catch (e) {} }
  }
};

const again = () => {
  clearTimeout(retry);
  retry = setTimeout(look, 5000);
};

const look = () => {
  connect().then(() => { if (!open && !mismatch) again(); });
};

chrome.alarms.create("mingent", { periodInMinutes: 0.5 });
chrome.alarms.onAlarm.addListener(look);
chrome.runtime.onStartup.addListener(look);
chrome.runtime.onInstalled.addListener(look);
chrome.action.onClicked.addListener(look);
look();
