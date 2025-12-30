// Renders a .docx into a container using docx-preview.
// Exposes a small API for Blazor JS interop.
(function () {
  function ensureDocxPreviewLoaded() {
    if (typeof window.docx === "undefined" || typeof window.docx.renderAsync !== "function") {
      throw new Error("docx-preview is not loaded (window.docx.renderAsync missing).");
    }
  }

  function escapeRegExp(s) {
    return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  }

  function highlight(containerId, highlights) {
    const root = document.getElementById(containerId);
    if (!root) throw new Error(`DOCX preview container not found: ${containerId}`);
    if (!Array.isArray(highlights) || highlights.length === 0) return;

    const items = highlights
      .filter((h) => h && typeof h.text === "string" && h.text.trim().length > 0)
      .map((h) => ({ text: h.text.trim(), cssClass: h.cssClass || "" }));
    if (items.length === 0) return;

    // Walk text nodes and wrap matches. Skip script/style and existing marks.
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
      acceptNode: (node) => {
        const p = node.parentElement;
        if (!p) return NodeFilter.FILTER_REJECT;
        const tag = p.tagName ? p.tagName.toLowerCase() : "";
        if (tag === "script" || tag === "style") return NodeFilter.FILTER_REJECT;
        if (p.closest("mark")) return NodeFilter.FILTER_REJECT;
        if (!node.nodeValue || !node.nodeValue.trim()) return NodeFilter.FILTER_REJECT;
        return NodeFilter.FILTER_ACCEPT;
      },
    });

    // Prebuild regexes (whole word-ish like our backend preview; culture invariant isn't available here).
    const regexes = items.map((it) => ({
      ...it,
      rx: new RegExp(`\\b${escapeRegExp(it.text)}\\b`, "gi"),
    }));

    const nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);

    for (const textNode of nodes) {
      const text = textNode.nodeValue;
      if (!text) continue;

      // Find earliest match among regexes; if none, skip.
      let best = null;
      for (const r of regexes) {
        r.rx.lastIndex = 0;
        const m = r.rx.exec(text);
        if (m && (best === null || m.index < best.index)) {
          best = { index: m.index, length: m[0].length, cssClass: r.cssClass, value: m[0] };
        }
      }
      if (!best) continue;

      // Build a fragment by repeatedly taking the earliest next match.
      const frag = document.createDocumentFragment();
      let pos = 0;
      let remaining = text;
      while (remaining.length) {
        let next = null;
        for (const r of regexes) {
          r.rx.lastIndex = 0;
          const m = r.rx.exec(remaining);
          if (m && (next === null || m.index < next.index)) {
            next = { index: m.index, length: m[0].length, cssClass: r.cssClass, value: m[0] };
          }
        }
        if (!next) {
          frag.appendChild(document.createTextNode(remaining));
          break;
        }
        if (next.index > 0) {
          frag.appendChild(document.createTextNode(remaining.slice(0, next.index)));
        }
        const mark = document.createElement("mark");
        if (next.cssClass) mark.className = next.cssClass;
        mark.textContent = next.value;
        frag.appendChild(mark);
        pos = next.index + next.length;
        remaining = remaining.slice(pos);
      }

      textNode.parentNode.replaceChild(frag, textNode);
    }
  }

  async function render(docxUrl, containerId) {
    ensureDocxPreviewLoaded();
    const el = document.getElementById(containerId);
    if (!el) throw new Error(`DOCX preview container not found: ${containerId}`);

    el.innerHTML = "<div class=\"text-muted small\">Loading document...</div>";

    try {
      const res = await fetch(docxUrl, { 
        cache: "no-cache",
        credentials: "include" // Include cookies for authentication
      });
      
      if (!res.ok) {
        if (res.status === 401) {
          throw new Error(`Unauthorized: Please ensure you are logged in (${res.status})`);
        } else if (res.status === 404) {
          throw new Error(`Document not found (${res.status})`);
        } else {
          throw new Error(`Failed to fetch DOCX (${res.status}): ${docxUrl}`);
        }
      }
      
      const arrayBuffer = await res.arrayBuffer();

      await window.docx.renderAsync(arrayBuffer, el, null, {
        className: "docx",
        inWrapper: true,
        ignoreWidth: false,
        ignoreHeight: true,
        ignoreFonts: false,
        breakPages: false,
        useBase64URL: true,
      });
    } catch (error) {
      el.innerHTML = `<div class="alert alert-danger">Error loading document: ${error.message}</div>`;
      throw error;
    }
  }

  window.bedrockDocxPreview = {
    render,
    highlight,
  };
})();
