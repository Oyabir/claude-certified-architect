// Console PME de PC Santé : React 18 + htm (JSX sans compilation). Aucun innerHTML : React échappe tout le texte.
(() => {
  "use strict";
  const { useState, useEffect, useCallback } = React;
  const html = htm.bind(React.createElement);
  const I18N = window.PCS_I18N;

  // ---- Utilitaires -------------------------------------------------------------------------------
  const storedLang = (() => { try { return localStorage.getItem("pcs-lang"); } catch { return null; } })();
  const browserLang = (navigator.language || "fr").slice(0, 2);
  const defaultLang = I18N[storedLang] ? storedLang : I18N[browserLang] ? browserLang : "fr";

  const format = (text, args) => text.replace(/\{(\d)\}/g, (_, i) => (args[i] ?? "").toString());
  const colorOf = score => score == null ? "Grey" : score >= 80 ? "Green" : score >= 50 ? "Orange" : "Red";

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

  // ---- Application --------------------------------------------------------------------------------
  function App() {
    const [lang, setLang] = useState(defaultLang);
    const [me, setMe] = useState(undefined);
    const t = useCallback((key, ...args) => format((I18N[lang] || I18N.fr)[key] ?? I18N.fr[key] ?? key, args), [lang]);

    useEffect(() => {
      document.documentElement.lang = lang;
      document.documentElement.dir = lang === "ar" ? "rtl" : "ltr";
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

  function LanguagePicker({ ctx }) {
    return html`<select aria-label=${ctx.t("language")} value=${ctx.lang} onChange=${e => ctx.setLang(e.target.value)}>
      <option value="fr">Français</option><option value="en">English</option><option value="ar">العربية</option>
    </select>`;
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
      <p class="brand">PC Santé</p>
      <p class="subtitle">${t("title")} — ${t("tagline")}</p>
      <form class="card" onSubmit=${submit}>
        <label for="email">${t("email")}</label>
        <input id="email" type="email" autocomplete="username" required value=${email} onInput=${e => setEmail(e.target.value)} />
        <label for="password">${t("password")}</label>
        <input id="password" type="password" autocomplete="current-password" required value=${password} onInput=${e => setPassword(e.target.value)} />
        ${error && html`<p class="error" role="alert">${error}</p>`}
        <p><button class="primary" type="submit">${t("signIn")}</button></p>
        <${LanguagePicker} ctx=${ctx} />
      </form>
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
      <h1>${t("changePassword")}</h1>
      <p class="subtitle">${t("changePasswordHelp")}</p>
      <form class="card" onSubmit=${submit}>
        <label for="current">${t("currentPassword")}</label>
        <input id="current" type="password" autocomplete="current-password" required value=${current} onInput=${e => setCurrent(e.target.value)} />
        <label for="replacement">${t("newPassword")}</label>
        <input id="replacement" type="password" autocomplete="new-password" minlength="10" required value=${replacement} onInput=${e => setReplacement(e.target.value)} />
        ${error && html`<p class="error" role="alert">${error}</p>`}
        <p><button class="primary" type="submit">${t("save")}</button></p>
      </form>
    </main>`;
  }

  function Shell({ ctx }) {
    const { t, me } = ctx;
    const [page, setPage] = useState({ name: "devices" });
    const [overview, setOverview] = useState(null);
    const owner = me.role === "Owner";
    useEffect(() => { api("GET", "/overview").then(setOverview).catch(() => setOverview(null)); }, [page]);
    const tabs = [["devices", "navDevices"], ["alerts", "navAlerts"], ["reports", "navReports"]].concat(owner ? [["org", "navOrganization"]] : []);
    const nav = { go: name => setPage({ name }), device: id => setPage({ name: "device", id }) };
    return html`<div>
      <header>
        <span class="org">${overview?.name ?? "PC Santé"}</span>
        <nav aria-label="${t("title")}">
          ${tabs.map(([name, key]) => html`<button key=${name} aria-current=${page.name === name || (name === "devices" && page.name === "device") ? "page" : undefined}
            onClick=${() => nav.go(name)}>${t(key)}</button>`)}
        </nav>
        <span class="muted">${me.displayName}${owner ? "" : " · " + t("viewer")}</span>
        <${LanguagePicker} ctx=${ctx} />
        <button class="secondary" onClick=${ctx.signOut}>${t("signOut")}</button>
      </header>
      <main>
        ${page.name === "devices" && html`<${Devices} ctx=${ctx} overview=${overview} nav=${nav} />`}
        ${page.name === "device" && html`<${DeviceDetail} ctx=${ctx} id=${page.id} nav=${nav} owner=${owner} />`}
        ${page.name === "alerts" && html`<${Alerts} ctx=${ctx} nav=${nav} owner=${owner} />`}
        ${page.name === "reports" && html`<${Reports} ctx=${ctx} />`}
        ${page.name === "org" && owner && html`<${Organization} ctx=${ctx} overview=${overview} />`}
      </main>
    </div>`;
  }

  const when = (ctx, value) => value ? new Date(value).toLocaleString(ctx.lang) : ctx.t("never");

  function Devices({ ctx, overview, nav }) {
    const { t } = ctx;
    const [devices, setDevices] = useState(null);
    useEffect(() => { api("GET", "/devices").then(setDevices).catch(() => setDevices([])); }, []);
    return html`<section>
      <h1>${t("devicesTitle")}</h1>
      ${overview && html`<div class="tiles">
        <div class="card tile"><div class="value">${overview.devices}</div><div class="label">${t("devices")} · ${t("seatsOf", overview.seats)}</div></div>
        <div class="card tile"><div class="value"><span class=${"badge " + colorOf(overview.averageScore)}>${overview.averageScore ?? "–"}</span></div><div class="label">${t("averageScore")}</div></div>
        <div class="card tile"><div class="value">${overview.openAlerts}</div><div class="label">${t("openAlerts")}</div></div>
        <div class="card tile"><div class="value">${overview.red}</div><div class="label">${t("redDevices")}</div></div>
        <div class="card tile"><div class="value">${overview.silent}</div><div class="label">${t("silentDevices")}</div></div>
      </div>`}
      ${devices && devices.length === 0 && html`<p class="card">${t("noDevices")}</p>`}
      ${devices && devices.length > 0 && html`<table>
        <thead><tr><th>${t("machine")}</th><th>${t("score")}</th><th>${t("lastContact")}</th><th>${t("alerts")}</th></tr></thead>
        <tbody>${devices.map(d => html`<tr key=${d.id} class="clickable" tabindex="0" onClick=${() => nav.device(d.id)}
            onKeyDown=${e => (e.key === "Enter" || e.key === " ") && nav.device(d.id)}>
          <td>${d.machineName}</td>
          <td><span class=${"badge " + (d.color ?? "Grey")}>${d.score ?? "–"}</span></td>
          <td>${when(ctx, d.lastReportAt)}${d.silent ? html` <span class="error">· ${t("silent")}</span>` : ""}</td>
          <td>${d.openAlerts}</td>
        </tr>`)}</tbody>
      </table>`}
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
    const sub = [["security", detail.security], ["performance", detail.performance], ["stability", detail.stability], ["storage", detail.storage]];
    return html`<section>
      <p><button class="secondary" onClick=${() => nav.go("devices")}>${t("back")}</button></p>
      <h1>${d.machineName} <span class=${"badge " + (d.color ?? "Grey")}>${d.score ?? "–"}</span></h1>
      <p class="muted">${t("lastContact")} : ${when(ctx, d.lastReportAt)} · ${t("windows")} : ${detail.windowsVersion ?? "–"} · ${t("appVersion")} : ${detail.appVersion ?? "–"}</p>
      <div class="tiles">${sub.map(([key, value]) => html`<div key=${key} class="card tile">
        <div class="value"><span class=${"badge " + colorOf(value)}>${value ?? "–"}</span></div><div class="label">${t(key)}</div></div>`)}</div>
      <h2>${t("issues")}</h2>
      ${detail.issues.length === 0 && html`<p class="card success">${t("noIssues")}</p>`}
      ${detail.issues.map((i, n) => html`<div key=${n} class=${"card stripe " + i.severity}><strong>${i.title}</strong><div>${i.why}</div></div>`)}
      ${owner && html`<p><button class="danger" onClick=${remove}>${t("removeDevice")}</button></p>`}
    </section>`;
  }

  function Alerts({ ctx, nav, owner }) {
    const { t } = ctx;
    const [alerts, setAlerts] = useState(null);
    const load = useCallback(() => api("GET", `/alerts?lang=${ctx.lang}`).then(setAlerts).catch(() => setAlerts([])), [ctx.lang]);
    useEffect(() => { load(); }, [load]);
    const severity = kind => kind === "Silent" ? "Warning" : "Critical";
    return html`<section>
      <h1>${t("alertsTitle")}</h1>
      ${alerts && alerts.length === 0 && html`<p class="card success">${t("noAlerts")}</p>`}
      ${(alerts || []).map(a => html`<div key=${a.id} class=${"card stripe " + severity(a.kind)}>
        <strong><a href="#" onClick=${e => { e.preventDefault(); nav.device(a.deviceId); }}>${a.machineName}</a></strong>
        <div>${a.text}</div>
        <div class="row"><span class="muted">${when(ctx, a.createdAt)}</span>
          ${owner && html`<button class="secondary" onClick=${() => api("POST", `/alerts/${a.id}/resolve`).then(load)}>${t("resolve")}</button>`}</div>
      </div>`)}
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
    return html`<section>
      <h1>${t("reportsTitle")}</h1>
      <div class="row">
        <label for="month" class="inline">${t("month")}</label>
        <input id="month" type="month" value=${month} onInput=${e => setMonth(e.target.value)} />
        <button class="primary" onClick=${show}>${t("show")}</button>
        <button class="secondary" onClick=${download}>${t("downloadCsv")}</button>
      </div>
      ${error && html`<p class="error" role="alert">${error}</p>`}
      ${report && html`<div>
        <div class="tiles">
          <div class="card tile"><div class="value">${report.devices}</div><div class="label">${t("devices")}</div></div>
          <div class="card tile"><div class="value"><span class=${"badge " + colorOf(report.averageScore)}>${report.averageScore ?? "–"}</span></div><div class="label">${t("monthAverage")}</div></div>
          <div class="card tile"><div class="value">${report.openAlerts}</div><div class="label">${t("openAlerts")}</div></div>
        </div>
        <table><thead><tr><th>${t("machine")}</th><th>${t("lastScore")}</th><th>${t("monthAverage")}</th><th>${t("reportsCount")}</th><th>${t("openAlerts")}</th></tr></thead>
        <tbody>${report.rows.map(r => html`<tr key=${r.deviceId}><td>${r.machineName}</td>
          <td><span class=${"badge " + colorOf(r.lastScore)}>${r.lastScore ?? "–"}</span></td>
          <td>${r.averageScore ?? "–"}</td><td>${r.reports}</td><td>${r.openAlerts}</td></tr>`)}</tbody></table>
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
    return html`<section>
      <h1>${t("orgTitle")}</h1>
      ${overview && html`<div class="card"><strong>${t("seats")}</strong> : ${overview.devices} ${t("used")} ${t("seatsOf", overview.seats)}</div>`}
      <h2>${t("enrollmentCode")}</h2>
      <div class="card">
        ${code ? html`<p>${t("newCodeShown")} <span class="secret">${code}</span></p>` : html`<p class="muted">${t("codeHint", overview?.enrollmentCodeHint ?? "")}</p>`}
        <button class="secondary" onClick=${rotate}>${t("newCode")}</button>
      </div>
      <h2>${t("users")}</h2>
      <table><thead><tr><th>${t("name")}</th><th>${t("email")}</th><th>${t("role")}</th><th>${t("lastLogin")}</th><th></th></tr></thead>
        <tbody>${users.map(u => html`<tr key=${u.id}><td>${u.displayName}</td><td>${u.email}</td><td>${t(u.role)}</td><td>${when(ctx, u.lastLoginAt)}</td>
          <td>${u.email !== ctx.me.email && html`<button class="secondary" onClick=${() => remove(u)}>${t("remove")}</button>`}</td></tr>`)}</tbody></table>
      <form class="card spaced" onSubmit=${add}>
        <div class="row">
          <input aria-label=${t("name")} placeholder=${t("name")} required value=${form.displayName} onInput=${e => setForm({ ...form, displayName: e.target.value })} />
          <input aria-label=${t("email")} placeholder=${t("email")} type="email" required value=${form.email} onInput=${e => setForm({ ...form, email: e.target.value })} />
          <select aria-label=${t("role")} value=${form.role} onChange=${e => setForm({ ...form, role: e.target.value })}>
            <option value="Viewer">${t("Viewer")}</option><option value="Owner">${t("Owner")}</option>
          </select>
          <button class="primary" type="submit">${t("addUser")}</button>
        </div>
        ${message && html`<p class=${message.ok ? "success" : "error"} role="status">${message.text} ${message.secret && html`<span class="secret">${message.secret}</span>`}</p>`}
      </form>
    </section>`;
  }

  ReactDOM.createRoot(document.getElementById("root")).render(html`<${App} />`);
})();
