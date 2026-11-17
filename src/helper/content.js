(() => {
  if (self.mingentHelper) return;
  self.mingentHelper = true;

  const allowed = url => url.startsWith(location.origin + "/_/BardChatUi/") || url === "https://content-push.googleapis.com/upload";

  const bytes = text => {
    const raw = atob(text);
    const out = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
    return out;
  };

  const run = async (port, jobs, job) => {
    const stop = new AbortController();
    jobs[job.id] = stop;
    const post = part => {
      part.type = "chunk";
      part.id = job.id;
      try { port.postMessage(part); } catch (e) {}
    };
    try {
      if (!allowed(job.url)) throw new Error("That address is not one the helper may call");
      const init = { method: "POST", headers: job.headers || {}, signal: stop.signal };
      if (job.file) {
        const form = new FormData();
        form.append("file", new Blob([bytes(job.file.data)], { type: job.file.type }), job.file.name);
        init.body = form;
      } else init.body = job.body;
      const reply = await fetch(job.url, init);
      const decoder = new TextDecoder();
      if (reply.body) {
        const reader = reply.body.getReader();
        for (;;) {
          const part = await reader.read();
          if (part.done) break;
          const text = decoder.decode(part.value, { stream: true });
          if (text) post({ t: text, status: reply.status });
        }
      }
      post({ t: decoder.decode(), status: reply.status, done: true });
    } catch (e) {
      post({ done: true, err: String((e && e.message) || e) });
    }
    delete jobs[job.id];
  };

  chrome.runtime.onConnect.addListener(port => {
    if (port.name !== "mingent") return;
    const jobs = {};
    port.onMessage.addListener(message => {
      if (message.type === "start") run(port, jobs, message);
      else if (message.type === "end" && jobs[message.id]) {
        try { jobs[message.id].abort(); } catch (e) {}
        delete jobs[message.id];
      }
    });
    port.onDisconnect.addListener(() => {
      for (const id in jobs) { try { jobs[id].abort(); } catch (e) {} }
    });
  });
})();
