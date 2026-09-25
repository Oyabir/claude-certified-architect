// Console PME de PC Santé : React 18 + htm (JSX sans compilation). Aucun innerHTML : React échappe tout le texte.
// Présentation : docs/redesign/HANDOFF.md § 9 (en-tête sur une ligne, chiffres clés, tableau cliquable, panneau « À traiter »).
(() => {
  "use strict";
  const { useState, useEffect, useCallback, useRef } = React;
  const html = htm.bind(React.createElement);
  const I18N = window.PCS_I18N;

  // ---- Utilitaires -------------------------------------------------------------------------------
  const storedLang = (() => { try { return localStorage.getItem("pcs-lang"); } catch { return null; } })();
  const browserLang = (navigator.language || "fr").slice(0, 2);
  const defaultLang = I18N[storedLang] ? storedLang : I18N[browserLang] ? browserLang : "fr";

  const format = (text, args) => text.replace(/\{(\d)\}/g, (_, i) => (args[i] ?? "").toString());
  // Mêmes seuils que l'application (vert ≥ 80, orange ≥ 50).
  const colorOf = score => score == null ? "Grey" : score >= 80 ? "Green" : score >= 50 ? "Orange" : "Red";
  const toneOf = color => ({ Green: "good", Orange: "warn", Red: "critical" })[color] || "none";
  const statusKey = color => ({ Green: "statusGreen", Orange: "statusOrange", Red: "statusRed" })[color] || "statusNone";
  // Chiffres latins et heures sans secondes, y compris en arabe (« 11:48 » et non « 11:48:05 ص »).
  const locale = lang => `${lang === "ar" ? "ar-MA" : lang}-u-nu-latn`;

  async function readJson(response) {
    try { return await response.json(); } catch { return null; }
  }

  // Toutes les requêtes portent l'en-tête anti-CSRF exigé par la console.
  async function api(method, path, body) {
    const response = await fetch("/api/console" + path, {
      method,
      credentials: "same-origin",
      headers: Object.assign({ "X-PcSante-Console": "1" }, body ? { "Content-Type": "application/json" } : {}),
      body: body ? JSON.stringify(body) : undefined
    });
    if (!response.ok) {
      const error = new Error("http " + response.status);
      error.status = response.status;
      error.body = await readJson(response);
      throw error;
    }
    if (response.status === 204) return null;
    const type = response.headers.get("Content-Type") || "";
    return type.includes("json") ? response.json() : response.blob();
  }

  // Icônes au trait (docs/redesign/assets/icons/icons.json).
  const ICONS = {
    monitor: "M3 4h18v12H3zM8 20h8M12 16v4",
    alert: "M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0zM12 9v4M12 17h.01",
    alertCircle: "M12 8v5M12 16.5h.01M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z",
    chevronRight: "m9 6 6 6-6 6",
    chevronDown: "m6 9 6 6 6-6",
    globe: "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18",
    plus: "M12 5v14M5 12h14",
    check: "M20 6 9 17l-5-5",
    x: "M18 6 6 18M6 6l12 12",
    shield: "M12 3 4 6v6c0 4.5 3.4 8.3 8 9 4.6-.7 8-4.5 8-9V6z",
    pulse: "M3 12h4l3-8 4 16 3-8h4",
    checkCircle: "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM8 12l3 3 5-6",
    drive: "M22 12H2M5.5 5.1 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.5-6.9A2 2 0 0 0 16.8 4H7.2a2 2 0 0 0-1.7 1.1zM6 16h.01M10 16h.01",
    download: "M12 3v12M7 10l5 5 5-5M5 21h14",
    logout: "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9"
  };
  // Icônes directionnelles (flèches, chevrons) retournées en arabe ; les autres ne le sont jamais.
  const Icon = ({ name, small, dir, cls }) => {
    const flip = dir && document.documentElement.dir === "rtl";
    return html`<svg class=${"icon" + (small ? " icon--sm" : "") + (cls ? " " + cls : "")}
      viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d=${ICONS[name]} transform=${flip ? "matrix(-1 0 0 1 24 0)" : undefined} /></svg>`;
  };
  const statusIcon = tone => ({ good: "check", warn: "alertCircle", critical: "x" })[tone];

  // Anneau de score (antihoraire en arabe par la feuille de style).
  function Ring({ value, size = 56, stroke = 7, label }) {
    const r = (size - stroke) / 2;
    const c = 2 * Math.PI * r;
    const tone = toneOf(colorOf(value));
    const fraction = value == null ? 0 : Math.max(0, Math.min(100, value)) / 100;
    return html`<svg class="ring" width=${size} height=${size} viewBox=${`0 0 ${size} ${size}`} role="img" aria-label=${label}>
      <circle class="track" cx=${size / 2} cy=${size / 2} r=${r} stroke-width=${stroke} />
      ${value != null && html`<circle class=${"arc " + tone} cx=${size / 2} cy=${size / 2} r=${r} stroke-width=${stroke}
        stroke-dasharray=${`${c * fraction} ${c}`} />`}
      <text x="50%" y="50%" dominant-baseline="central" text-anchor="middle">${value ?? "–"}</text>
    </svg>`;
  }

  // Puce de score + libellé d'état (couleur, mot et chiffre : jamais la couleur seule).
  function State({ ctx, score, color }) {
    const c = color ?? colorOf(score);
    const tone = toneOf(c);
    return html`<span class=${"state " + tone}>
      <span class=${"pcs-score pcs-score--" + tone}>${score ?? "–"}</span>${ctx.t(statusKey(c))}
    </span>`;
  }

  // ---- Application --------------------------------------------------------------------------------
  function App() {
    const [lang, setLang] = useState(defaultLang);
    const [me, setMe] = useState(undefined);
    const t = useCallback((key, ...args) => format((I18N[lang] || I18N.fr)[key] ?? I18N.fr[key] ?? key, args), [lang]);

    // Sens de lecture fixé avant le rendu des enfants (les icônes directionnelles en dépendent).
    document.documentElement.dir = lang === "ar" ? "rtl" : "ltr";
    useEffect(() => {
      document.documentElement.lang = lang;
      document.title = "PC Santé — " + t("title");
      try { localStorage.setItem("pcs-lang", lang); } catch { /* stockage indisponible */ }
    }, [lang, t]);

    const refreshMe = useCallback(() => api("GET", "/me").then(setMe).catch(() => setMe(null)), []);
    useEffect(() => { refreshMe(); }, [refreshMe]);

    const ctx = { t, lang, setLang, me, refreshMe, signOut: () => api("POST", "/logout").finally(() => setMe(null)) };
    if (me === undefined) return null;
    if (!me) return html`<${Login} ctx=${ctx} />`;
    if (me.mustChangePassword) return html`<${ChangePassword} ctx=${ctx} />`;
    return html`<${Shell} ctx=${ctx} />`;
  }

  // Sélecteur de langue compact (globe + code).
  function LanguagePicker({ ctx }) {
    return html`<label class="lang"><${Icon} name="globe" small />
      <span class="sr-only">${ctx.t("language")}</span>
      <select aria-label=${ctx.t("language")} value=${ctx.lang} onChange=${e => ctx.setLang(e.target.value)}>
        <option value="fr">FR · Français</option><option value="en">EN · English</option><option value="ar">AR · العربية</option>
      </select>
      <${Icon} name="chevronDown" small cls="chev" />
    </label>`;
  }

  function Logo({ ctx, sub }) {
    return html`<div class="brand"><img src="img/logo.png" alt="" /><div><strong>PC Santé</strong>${sub && html`<span>${sub}</span>`}</div></div>`;
  }

  function Login({ ctx }) {
    const { t } = ctx;
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [error, setError] = useState("");
    const submit = async e => {
      e.preventDefault();
      setError("");
      try {
        await api("POST", "/login", { email, password });
        await ctx.refreshMe();
      } catch (err) {
        setError(err.body?.error === "locked" ? t("locked") : t("badLogin"));
      }
    };
    return html`<main class="login">
      <div class="logo"><img src="img/logo.png" alt="" /><div><strong>PC Santé</strong><div class="muted">${t("title")}</div></div></div>
      <form class="card" onSubmit=${submit}>
        <p class="muted">${t("tagline")}</p>
        <label for="email">${t("email")}</label>
        <input id="email" type="email" autocomplete="username" required value=${email} onInput=${e => setEmail(e.target.value)} />
        <label for="password">${t("password")}</label>
        <input id="password" type="password" autocomplete="current-password" required value=${password} onInput=${e => setPassword(e.target.value)} />
        ${error && html`<p class="error" role="alert">${error}</p>`}
        <button class="pcs-btn pcs-btn--primary" type="submit">${t("signIn")}</button>
      </form>
      <${LanguagePicker} ctx=${ctx} />
    </main>`;
  }

  function ChangePassword({ ctx }) {
    const { t } = ctx;
    const [current, setCurrent] = useState("");
    const [replacement, setReplacement] = useState("");
    const [error, setError] = useState("");
    const submit = async e => {
      e.preventDefault();
      try {
        await api("POST", "/me/password", { current, replacement });
        await ctx.refreshMe();
      } catch {
        setError(t("passwordRefused"));
      }
    };
    return html`<main class="login">
      <div class="logo"><img src="img/logo.png" alt="" /><strong>${t("changePassword")}</strong></div>
      <form class="card" onSubmit=${submit}>
        <p class="muted">${t("changePasswordHelp")}</p>
        <label for="current">${t("currentPassword")}</label>
        <input id="current" type="password" autocomplete="current-password" required value=${current} onInput=${e => setCurrent(e.target.value)} />
        <label for="replacement">${t("newPassword")}</label>
        <input id="replacement" type="password" autocomplete="new-password" minlength="10" required value=${replacement} onInput=${e => setReplacement(e.target.value)} />
        ${error && html`<p class="error" role="alert">${error}</p>`}
        <button class="pcs-btn pcs-btn--primary" type="submit">${t("save")}</button>
      </form>
    </main>`;
  }

  // Menu du compte : avatar + adresse, « Se déconnecter » dedans.
  function Account({ ctx }) {
    const { t, me } = ctx;
    const [open, setOpen] = useState(false);
    const ref = useRef(null);
    useEffect(() => {
      if (!open) return undefined;
      const close = e => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
      const esc = e => { if (e.key === "Escape") setOpen(false); };
      document.addEventListener("mousedown", close);
      document.addEventListener("keydown", esc);
      return () => { document.removeEventListener("mousedown", close); document.removeEventListener("keydown", esc); };
    }, [open]);
    const initial = (me.displayName || me.email || "?").trim().charAt(0).toUpperCase();
    return html`<div class="account" ref=${ref}>
      <button aria-haspopup="menu" aria-expanded=${open} aria-label=${t("accountMenu")} onClick=${() => setOpen(!open)}>
        <span class="avatar">${initial}</span><span class="email ltr">${me.email}</span><${Icon} name="chevronDown" small />
      </button>
      ${open && html`<div class="menu" role="menu">
        <p><strong>${me.displayName}</strong><br /><span class="muted">${t(me.role === "Owner" ? "Owner" : "Viewer")}</span></p>
        <button role="menuitem" onClick=${ctx.signOut}><${Icon} name="logout" small dir />${t("signOut")}</button>
      </div>`}
    </div>`;
  }

  function Shell({ ctx }) {
    const { t, me } = ctx;
    const [page, setPage] = useState({ name: "devices" });
    const [overview, setOverview] = useState(null);
    const owner = me.role === "Owner";
    const loadOverview = useCallback(() => api("GET", "/overview").then(setOverview).catch(() => setOverview(null)), []);
    useEffect(() => { loadOverview(); }, [page, loadOverview]);
    const tabs = [["devices", "navDevices"], ["alerts", "navAlerts"], ["reports", "navReports"]].concat(owner ? [["org", "navOrganization"]] : []);
    const nav = { go: name => setPage({ name }), device: id => setPage({ name: "device", id }), refresh: loadOverview };
    return html`<div>
      <header class="top">
        <${Logo} ctx=${ctx} sub=${overview?.name} />
        <nav class="pcs-nav" aria-label=${t("title")}>
          ${tabs.map(([name, key]) => html`<button key=${name} aria-current=${page.name === name || (name === "devices" && page.name === "device") ? "page" : undefined}
            onClick=${() => nav.go(name)}>${t(key)}${name === "alerts" && overview?.openAlerts > 0
              ? html`<span class="count num" aria-label=${t("openAlertsCount", overview.openAlerts)}>${overview.openAlerts}</span>` : ""}</button>`)}
        </nav>
        <${LanguagePicker} ctx=${ctx} />
        <${Account} ctx=${ctx} />
      </header>
      <main class="page">
        ${!owner && html`<p class="muted">${t("viewer")}</p>`}
        ${page.name === "devices" && html`<${Devices} ctx=${ctx} overview=${overview} nav=${nav} owner=${owner} />`}
        ${page.name === "device" && html`<${DeviceDetail} ctx=${ctx} id=${page.id} nav=${nav} owner=${owner} />`}
        ${page.name === "alerts" && html`<${Alerts} ctx=${ctx} nav=${nav} owner=${owner} />`}
        ${page.name === "reports" && html`<${Reports} ctx=${ctx} />`}
        ${page.name === "org" && owner && html`<${Organization} ctx=${ctx} overview=${overview} />`}
      </main>
    </div>`;
  }

  // Dates au format de la langue, sans secondes : « Aujourd'hui, 11:48 », « Hier, 22:20 », sinon « 24/09/2026 11:48 ».
  const when = (ctx, value) => {
    if (!value) return ctx.t("never");
    const d = new Date(value);
    const time = new Intl.DateTimeFormat(locale(ctx.lang), { hour: "2-digit", minute: "2-digit" }).format(d);
    const day = x => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
    const today = day(new Date());
    if (day(d) === today) return ctx.t("todayAt", time);
    if (day(d) === today - 86400000) return ctx.t("yesterdayAt", time);
    return new Intl.DateTimeFormat(locale(ctx.lang), { dateStyle: "short", timeStyle: "short" }).format(d);
  };

  function AlertBox({ ctx, alert, nav, owner, onResolved }) {
    const warn = alert.kind === "Silent";
    return html`<div class=${"alert-box" + (warn ? " warn" : "")}>
      <div class="who"><${Icon} name="alert" small />
        <button class="ltr" onClick=${() => nav.device(alert.deviceId)}>${alert.machineName}</button><span class="num">· ${when(ctx, alert.createdAt)}</span></div>
      <p>${alert.text}</p>
      ${owner && html`<button class="pcs-btn pcs-btn--secondary pcs-btn--sm" onClick=${() => api("POST", `/alerts/${alert.id}/resolve`).then(onResolved)}>${ctx.t("resolve")}</button>`}
    </div>`;
  }

  function Devices({ ctx, overview, nav, owner }) {
    const { t } = ctx;
    const [devices, setDevices] = useState(null);
    const [alerts, setAlerts] = useState([]);
    const [loadedAt, setLoadedAt] = useState(null);
    const loadAlerts = useCallback(() => api("GET", `/alerts?lang=${ctx.lang}`).then(setAlerts).catch(() => setAlerts([])), [ctx.lang]);
    useEffect(() => {
      api("GET", "/devices").then(d => { setDevices(d); setLoadedAt(new Date()); }).catch(() => setDevices([]));
    }, []);
    useEffect(() => { loadAlerts(); }, [loadAlerts]);
    const resolved = () => { loadAlerts(); nav.refresh(); };
    const withAlerts = (devices || []).filter(d => d.openAlerts > 0);
    const seatsRatio = overview && overview.seats ? Math.min(100, 100 * overview.devices / overview.seats) : 0;
    const avgColor = colorOf(overview?.averageScore);
    return html`<section>
      <div class="page-head">
        <div>
          <h1>${t("devicesTitle")}</h1>
          ${overview && html`<p class="muted">${loadedAt && t("updatedAt", new Intl.DateTimeFormat(locale(ctx.lang), { hour: "2-digit", minute: "2-digit" }).format(loadedAt))} · ${t("devicesOfSeats", overview.devices, overview.seats)}</p>`}
        </div>
        ${owner && html`<button class="pcs-btn pcs-btn--primary" onClick=${() => nav.go("org")}><${Icon} name="plus" small />${t("addDevice")}</button>`}
      </div>

      ${overview && html`<div class="kpis">
        <div class="kpi">
          <div class="label">${t("trackedDevices")}</div>
          <div class="value">${overview.devices}<small>${t("ofLicences", overview.seats)}</small></div>
          <div class="bar" role="progressbar" aria-valuemin="0" aria-valuemax=${overview.seats} aria-valuenow=${overview.devices}><span style=${{ width: seatsRatio + "%" }}></span></div>
        </div>
        <div class="kpi kpi--row">
          <${Ring} value=${overview.averageScore} label=${t("averageScore")} />
          <div><div class="label">${t("averageScore")}</div>
            <div class=${"state " + toneOf(avgColor)}>${statusIcon(toneOf(avgColor)) && html`<${Icon} name=${statusIcon(toneOf(avgColor))} small />`}${t(statusKey(avgColor))}</div></div>
        </div>
        <div class="kpi">
          <div class="label">${t("openAlerts")}</div>
          <div class=${"value" + (overview.openAlerts > 0 ? " critical" : "")}>${overview.openAlerts}</div>
          <div class="note">${overview.openAlerts > 0 && withAlerts.length > 0
            ? t("onDevices", withAlerts.length, withAlerts.slice(0, 2).map(d => d.machineName).join(", ")) : t("noAlertsShort")}</div>
        </div>
        <div class="kpi">
          <div class="label">${t("silentDevices")}</div>
          <div class="value">${overview.silent}</div>
          <div class=${"note" + (overview.silent === 0 ? " good" : "")}>${overview.silent === 0 ? t("allAlive") : t("silentHelp")}</div>
        </div>
      </div>`}

      <div class="split">
        <div class="table-card">
          ${devices && devices.length === 0 && html`<div class="card"><p class="empty"><span class="tile-icon"><${Icon} name="monitor" /></span>${t("noDevices")}</p></div>`}
          ${devices && devices.length > 0 && html`<table class="pcs-table">
            <thead><tr><th>${t("machine")}</th><th>${t("state")}</th><th>${t("alerts")}</th><th>${t("lastContact")}</th><th><span class="sr-only">${t("open")}</span></th></tr></thead>
            <tbody>${devices.map(d => html`<tr key=${d.id} tabindex="0" onClick=${() => nav.device(d.id)}
                onKeyDown=${e => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); nav.device(d.id); } }}>
              <td><div class="machine"><span class="tile-icon"><${Icon} name="monitor" /></span><strong class="ltr">${d.machineName}</strong></div></td>
              <td><${State} ctx=${ctx} score=${d.score} color=${d.color} /></td>
              <td>${d.openAlerts > 0 ? html`<strong class="text-critical num">${d.openAlerts}</strong>` : html`<span class="text-muted">—</span>`}</td>
              <td><span class="num">${when(ctx, d.lastReportAt)}</span>${d.silent ? html` <span class="pcs-chip pcs-chip--warn">${t("silent")}</span>` : ""}</td>
              <td class="chev-cell"><${Icon} name="chevronRight" small dir /></td>
            </tr>`)}</tbody>
          </table>`}
        </div>

        <aside class="side" aria-label=${t("toHandle")}>
          <div class="side-head"><h2>${t("toHandle")}</h2>
            <button class="pcs-btn pcs-btn--ghost pcs-btn--sm" onClick=${() => nav.go("alerts")}>${t("allAlerts")}</button></div>
          ${alerts.length === 0 && html`<p class="empty"><span class="tile-icon good"><${Icon} name="check" /></span>${t("noAlertsShort")}</p>`}
          ${alerts.slice(0, 4).map(a => html`<${AlertBox} key=${a.id} ctx=${ctx} alert=${a} nav=${nav} owner=${owner} onResolved=${resolved} />`)}
        </aside>
      </div>
    </section>`;
  }

  function DeviceDetail({ ctx, id, nav, owner }) {
    const { t } = ctx;
    const [detail, setDetail] = useState(null);
    useEffect(() => { api("GET", `/devices/${id}?lang=${ctx.lang}`).then(setDetail).catch(() => nav.go("devices")); }, [id, ctx.lang]);
    if (!detail) return null;
    const d = detail.device;
    const remove = async () => {
      if (!window.confirm(t("removeConfirm", d.machineName))) return;
      await api("DELETE", `/devices/${id}`);
      nav.go("devices");
    };
    const sub = [["security", detail.security, "shield"], ["performance", detail.performance, "pulse"], ["stability", detail.stability, "checkCircle"], ["storage", detail.storage, "drive"]];
    const color = d.color ?? colorOf(d.score);
    const issueTone = s => ({ Critical: "critical", Warning: "warn" })[s] || "brand";
    return html`<section class="stack">
      <nav class="crumbs" aria-label=${t("breadcrumb")}>
        <button onClick=${() => nav.go("devices")}>${t("navDevices")}</button><${Icon} name="chevronRight" small dir /><span class="ltr">${d.machineName}</span>
      </nav>
      <div class="card detail-head">
        <${Ring} value=${d.score} size=${96} stroke=${10} label=${t("score")} />
        <div>
          <h1 class="ltr">${d.machineName}</h1>
          <p><span class=${"pcs-chip pcs-chip--" + (toneOf(color) === "none" ? "warn" : toneOf(color))}>
            ${statusIcon(toneOf(color)) && html`<${Icon} name=${statusIcon(toneOf(color))} small />`}${t(statusKey(color))}</span></p>
          <p class="muted">${t("lastContact")} : <span class="num">${when(ctx, d.lastReportAt)}</span> · ${t("windows")} : <span class="ltr">${detail.windowsVersion ?? "–"}</span> · ${t("appVersion")} : <span class="ltr">${detail.appVersion ?? "–"}</span></p>
        </div>
      </div>
      <div class="subscores">${sub.map(([key, value, icon]) => {
        const tone = toneOf(colorOf(value));
        return html`<div key=${key} class="subscore">
          <div class="top-line"><span class=${"tile-icon " + tone}><${Icon} name=${icon} small /></span><span style=${{ flex: 1 }}>${t(key)}</span><b class=${"text-" + (tone === "none" ? "muted" : tone)}>${value ?? "–"}</b></div>
          <div class=${"bar " + tone}><span style=${{ width: (value ?? 0) + "%" }}></span></div>
        </div>`;
      })}</div>
      <div class="card">
        <h2>${t("issues")}</h2>
        ${detail.issues.length === 0 && html`<p class="empty"><span class="tile-icon good"><${Icon} name="check" /></span>${t("noIssues")}</p>`}
        ${detail.issues.map((i, n) => html`<div key=${n} class="issue">
          <span class=${"tile-icon " + issueTone(i.severity)}><${Icon} name=${i.severity === "Info" ? "checkCircle" : "alert"} /></span>
          <div><strong>${i.title}</strong><div class="why">${i.why}</div></div>
        </div>`)}
      </div>
      ${owner && html`<p><button class="pcs-btn pcs-btn--danger" onClick=${remove}>${t("removeDevice")}</button></p>`}
    </section>`;
  }

  function Alerts({ ctx, nav, owner }) {
    const { t } = ctx;
    const [alerts, setAlerts] = useState(null);
    const load = useCallback(() => api("GET", `/alerts?lang=${ctx.lang}`).then(setAlerts).catch(() => setAlerts([])), [ctx.lang]);
    useEffect(() => { load(); }, [load]);
    return html`<section>
      <div class="page-head"><h1>${t("alertsTitle")}</h1></div>
      <div class="side" style=${{ maxWidth: "860px" }}>
        ${alerts && alerts.length === 0 && html`<p class="empty"><span class="tile-icon good"><${Icon} name="check" /></span>${t("noAlerts")}</p>`}
        ${(alerts || []).map(a => html`<${AlertBox} key=${a.id} ctx=${ctx} alert=${a} nav=${nav} owner=${owner} onResolved=${() => { load(); nav.refresh(); }} />`)}
      </div>
    </section>`;
  }

  function Reports({ ctx }) {
    const { t } = ctx;
    const now = new Date();
    const [month, setMonth] = useState(`${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}`);
    const [report, setReport] = useState(null);
    const [error, setError] = useState("");
    const [year, m] = month.split("-").map(Number);
    const show = () => api("GET", `/reports/${year}/${m}`).then(r => { setReport(r); setError(""); }).catch(() => setError(t("error")));
    useEffect(() => { show(); }, []);
    const download = async () => {
      const blob = await api("GET", `/reports/${year}/${m}/csv?lang=${ctx.lang}`);
      const link = document.createElement("a");
      link.href = URL.createObjectURL(blob);
      link.download = `pcsante-pme-${month}.csv`;
      link.click();
      setTimeout(() => URL.revokeObjectURL(link.href), 1000);
    };
    return html`<section class="stack">
      <div class="page-head">
        <h1>${t("reportsTitle")}</h1>
        <div class="row">
          <input id="month" type="month" aria-label=${t("month")} value=${month} onInput=${e => setMonth(e.target.value)} />
          <button class="pcs-btn pcs-btn--primary" onClick=${show}>${t("show")}</button>
          <button class="pcs-btn pcs-btn--secondary" onClick=${download}><${Icon} name="download" small />${t("downloadCsv")}</button>
        </div>
      </div>
      ${error && html`<p class="error" role="alert">${error}</p>`}
      ${report && html`<div class="stack">
        <div class="kpis">
          <div class="kpi"><div class="label">${t("devices")}</div><div class="value">${report.devices}</div></div>
          <div class="kpi kpi--row"><${Ring} value=${report.averageScore} label=${t("monthAverage")} /><div class="label">${t("monthAverage")}</div></div>
          <div class="kpi"><div class="label">${t("openAlerts")}</div><div class=${"value" + (report.openAlerts > 0 ? " critical" : "")}>${report.openAlerts}</div></div>
        </div>
        <div class="table-card"><table class="pcs-table static">
          <thead><tr><th>${t("machine")}</th><th>${t("lastScore")}</th><th>${t("monthAverage")}</th><th>${t("reportsCount")}</th><th>${t("openAlerts")}</th></tr></thead>
          <tbody>${report.rows.map(r => html`<tr key=${r.deviceId}><td><strong class="ltr">${r.machineName}</strong></td>
            <td><${State} ctx=${ctx} score=${r.lastScore} /></td>
            <td class="num">${r.averageScore ?? "–"}</td><td class="num">${r.reports}</td><td class="num">${r.openAlerts}</td></tr>`)}</tbody>
        </table></div>
      </div>`}
    </section>`;
  }

  function Organization({ ctx, overview }) {
    const { t } = ctx;
    const [users, setUsers] = useState([]);
    const [code, setCode] = useState("");
    const [form, setForm] = useState({ email: "", displayName: "", role: "Viewer" });
    const [message, setMessage] = useState(null);
    const load = useCallback(() => api("GET", "/users").then(setUsers).catch(() => setUsers([])), []);
    useEffect(() => { load(); }, [load]);
    const rotate = async () => {
      if (!window.confirm(t("newCodeConfirm"))) return;
      const result = await api("POST", "/organization/enrollment-code");
      setCode(result.enrollmentCode);
    };
    const add = async e => {
      e.preventDefault();
      try {
        const result = await api("POST", "/users", form);
        setMessage({ ok: true, text: t("tempPassword", form.email), secret: result.temporaryPassword });
        setForm({ email: "", displayName: "", role: "Viewer" });
        load();
      } catch {
        setMessage({ ok: false, text: t("userRefused") });
      }
    };
    const remove = async u => {
      if (!window.confirm(t("removeUserConfirm", u.displayName))) return;
      await api("DELETE", `/users/${u.id}`).catch(() => setMessage({ ok: false, text: t("error") }));
      load();
    };
    return html`<section class="stack">
      <div class="page-head"><div><h1>${t("orgTitle")}</h1>
        ${overview && html`<p class="muted">${t("devicesOfSeats", overview.devices, overview.seats)}</p>`}</div></div>
      <div class="card">
        <h2>${t("enrollmentCode")}</h2>
        <p class="muted">${t("addDeviceHelp")}</p>
        ${code ? html`<p>${t("newCodeShown")} <span class="secret">${code}</span></p>` : html`<p class="muted">${t("codeHint", overview?.enrollmentCodeHint ?? "")}</p>`}
        <button class="pcs-btn pcs-btn--secondary" onClick=${rotate}>${t("newCode")}</button>
      </div>
      <div class="card">
        <h2>${t("users")}</h2>
        <table class="pcs-table static" style=${{ marginTop: "12px" }}><thead><tr><th>${t("name")}</th><th>${t("email")}</th><th>${t("role")}</th><th>${t("lastLogin")}</th><th><span class="sr-only">${t("remove")}</span></th></tr></thead>
          <tbody>${users.map(u => html`<tr key=${u.id}><td><strong>${u.displayName}</strong></td><td class="ltr">${u.email}</td><td>${t(u.role)}</td><td class="num">${when(ctx, u.lastLoginAt)}</td>
            <td>${u.email !== ctx.me.email && html`<button class="pcs-btn pcs-btn--danger pcs-btn--sm" onClick=${() => remove(u)}>${t("remove")}</button>`}</td></tr>`)}</tbody></table>
        <form onSubmit=${add} style=${{ marginTop: "16px" }}>
          <div class="row">
            <input aria-label=${t("name")} placeholder=${t("name")} required value=${form.displayName} onInput=${e => setForm({ ...form, displayName: e.target.value })} />
            <input aria-label=${t("email")} placeholder=${t("email")} type="email" required value=${form.email} onInput=${e => setForm({ ...form, email: e.target.value })} />
            <select aria-label=${t("role")} value=${form.role} onChange=${e => setForm({ ...form, role: e.target.value })}>
              <option value="Viewer">${t("Viewer")}</option><option value="Owner">${t("Owner")}</option>
            </select>
            <button class="pcs-btn pcs-btn--primary" type="submit"><${Icon} name="plus" small />${t("addUser")}</button>
          </div>
          ${message && html`<p class=${message.ok ? "success" : "error"} role="status">${message.text} ${message.secret && html`<span class="secret">${message.secret}</span>`}</p>`}
        </form>
      </div>
    </section>`;
  }

  ReactDOM.createRoot(document.getElementById("root")).render(html`<${App} />`);
})();
