// 메이플 바 하루 사용자 세기 (Cloudflare Worker + Durable Object)
// POST /hit    → 오늘(한국 시간) 수를 1 올린다. 바는 하루에 한 번만 보낸다
// GET  /count  헤더 X-Key: 비밀 열쇠 → {"today":오늘 수, "days":[{"day":"YYYY-MM-DD","n":수}, ...] (최근 30일, 최신 먼저),
//              "hoursToday":[24칸, 오늘 몇 시에 켰는지], "hours":[24칸, 최근 30일 합]}
// 비밀 열쇠는 코드에 넣지 않고 `npx wrangler secret put ADMIN_KEY`로 넣는다.
//
// 설정 옮기기 (같은 클래스의 다른 인스턴스 "cfg", SQLite 표). 모두 POST, 본문은 JSON, 답도 JSON.
// POST /cfg/status {}                → {ok, q, full}
// POST /cfg/save   {name, pw, data}  → {ok, expires, q}   (같은 이름은 비밀번호가 같을 때만 덮어쓴다)
// POST /cfg/load   {name, pw}        → {ok, data, expires, q}
// POST /cfg/extend {name, pw}        → {ok, expires, q}   (30일 연장, 저장 횟수로 센다)
// 실패하면 {ok:false, err, sec?, q?, full}. err: busy·wait·daily·taken·wrong·locked·tries·bad
// q = {save:{left, wait}, load:{left, wait}}: 오늘 남은 횟수, 다시 할 수 있을 때까지 초
// full = {save, load}: 서버 전체 하루 한도를 넘었는지
// IP는 그날의 무작위 비밀값으로 해시해서만 쓰고 원본은 저장하지 않는다. 비밀값은 날마다 새로 만든다.

const KEEP = 30; // 돌려줄 날 수

const CFG = {
  bodyMax: 16 * 1024,        // 본문 크기 한도
  dataMax: 14 * 1024,        // 설정 묶음(압축한 글자) 한도
  keepDays: 30,              // 저장한 설정을 지우기까지
  perDay: 3,                 // 같은 PC(IP) 하루 저장·불러오기 각각
  waitSec: 30 * 60,          // 한 번 하고 다음까지
  saveCap: 5000,             // 서버 전체 하루 저장 (무료 한도 보호)
  loadCap: 20000,            // 서버 전체 하루 불러오기
  failPerDay: 20,            // 같은 PC 하루 비밀번호 틀림
  nameFails: 5,              // 같은 이름 비밀번호를 이만큼 틀리면
  nameLockSec: 15 * 60,      // 이만큼 잠근다
  iter: 100000,              // PBKDF2 반복 (Workers 최대값)
};

export default {
  async fetch(req, env) {
    const p = new URL(req.url).pathname;
    if (req.method === "POST" && /^\/cfg\/(status|save|load|extend)$/.test(p)) {
      // 바깥에서 온 머리글은 버리고, 클라우드플레어가 알려 준 IP만 안으로 넘긴다 (안에서 해시만 하고 버린다)
      const len = Number(req.headers.get("Content-Length") || 0);
      if (len > CFG.bodyMax) return Response.json({ ok: false, err: "bad" }, { status: 413 });
      const body = await req.text();
      if (body.length > CFG.bodyMax) return Response.json({ ok: false, err: "bad" }, { status: 413 });
      const inner = new Request("https://cfg" + p, {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-Ip": req.headers.get("CF-Connecting-IP") || "" },
        body,
      });
      return env.COUNTER.get(env.COUNTER.idFromName("cfg")).fetch(inner);
    }
    const ok = (req.method === "POST" && p === "/hit") || (req.method === "GET" && p === "/count");
    if (!ok) return new Response("not found", { status: 404 });
    return env.COUNTER.get(env.COUNTER.idFromName("all")).fetch(req);
  },
};

export class Counter {
  constructor(state, env) {
    this.state = state;
    this.env = env;
  }

  async fetch(req) {
    const p = new URL(req.url).pathname;
    if (p.startsWith("/cfg/")) return this.cfg(req, p.slice(5));
    const today = kstDay(Date.now());

    if (p === "/hit") {
      // 신호가 온 시각(한국 시간)으로 몇 시에 켰는지도 센다. 바는 시간을 따로 보내지 않는다
      const hour = new Date(Date.now() + 9 * 3600 * 1000).getUTCHours();
      const k = "d:" + today, hk = "h:" + today;
      const hours = (await this.state.storage.get(hk)) || new Array(24).fill(0);
      hours[hour]++;
      await this.state.storage.put({ [k]: ((await this.state.storage.get(k)) || 0) + 1, [hk]: hours });
      return new Response("ok");
    }

    const key = req.headers.get("X-Key") || "";
    if (!this.env.ADMIN_KEY || key !== this.env.ADMIN_KEY) return new Response("forbidden", { status: 403 });
    const all = await this.state.storage.list({ prefix: "d:", reverse: true, limit: KEEP });
    const days = [...all].map(([k, n]) => ({ day: k.slice(2), n }));
    const hs = await this.state.storage.list({ prefix: "h:", reverse: true, limit: KEEP });
    const hours = new Array(24).fill(0);
    for (const [, a] of hs) for (let i = 0; i < 24; i++) hours[i] += a[i] || 0;
    return Response.json({ today: all.get("d:" + today) || 0, days, hoursToday: hs.get("h:" + today) || new Array(24).fill(0), hours });
  }

  // ── 설정 옮기기 ("cfg" 인스턴스만 쓴다) ──

  db() {
    const sql = this.state.storage.sql;
    if (!this.ready) {
      sql.exec("CREATE TABLE IF NOT EXISTS cfg(k TEXT PRIMARY KEY, salt TEXT, hash TEXT, data TEXT, expires INTEGER, fails INTEGER DEFAULT 0, lock_until INTEGER DEFAULT 0)");
      sql.exec("CREATE TABLE IF NOT EXISTS quota(day TEXT, who TEXT, kind TEXT, n INTEGER, last INTEGER, PRIMARY KEY(day, who, kind))");
      sql.exec("CREATE TABLE IF NOT EXISTS totals(day TEXT, kind TEXT, n INTEGER, PRIMARY KEY(day, kind))");
      sql.exec("CREATE TABLE IF NOT EXISTS salts(day TEXT PRIMARY KEY, v TEXT)");
      this.ready = true;
    }
    return sql;
  }
  one(q, ...a) { return this.db().exec(q, ...a).toArray()[0]; }
  run(q, ...a) { this.db().exec(q, ...a); }

  async cfg(req, op) {
    const day = kstDay(Date.now());
    if (!this.alarmSet) {
      if ((await this.state.storage.getAlarm()) == null) await this.state.storage.setAlarm(Date.now() + 86400000);
      this.alarmSet = true;
    }
    const who = await this.who(req.headers.get("X-Ip") || "", day);
    const reply = (o) => Response.json({ ...o, full: this.full(day), q: this.quota(day, who, Date.now()) });

    let b;
    try { b = JSON.parse((await req.text()) || "{}"); } catch { return reply({ ok: false, err: "bad" }); }
    if (!b || typeof b !== "object" || Array.isArray(b)) return reply({ ok: false, err: "bad" });
    if (op === "status") return reply({ ok: true });

    const kind = op === "load" ? "load" : "save";
    const now = Date.now();
    if (this.full(day)[kind]) return reply({ ok: false, err: "busy" });
    const gate = this.gate(day, who, kind, now);
    if (gate) return reply(gate);
    if (this.count(day, who, "fail") >= CFG.failPerDay) return reply({ ok: false, err: "tries" });

    const name = cleanName(b.name), pw = typeof b.pw === "string" ? b.pw : "";
    if (!name || pw.length < 4 || pw.length > 32) return reply({ ok: false, err: "bad" });
    let data = null;
    if (op === "save") {
      if (typeof b.data !== "string" || !b.data || b.data.length > CFG.dataMax || !/^[A-Za-z0-9+/]+=*$/.test(b.data))
        return reply({ ok: false, err: "bad" });
      data = b.data;
    }
    const k = name.normalize("NFC").toLowerCase();
    this.run("DELETE FROM cfg WHERE k = ? AND expires <= ?", k, now);
    const before = this.one("SELECT * FROM cfg WHERE k = ?", k);
    if (before && before.lock_until > now) return reply({ ok: false, err: "locked", sec: Math.ceil((before.lock_until - now) / 1000) });

    // 비밀번호 확인·해시 (기다리는 동안 다른 요청이 끼어들 수 있다)
    let salt = null, hash = null;
    if (before) {
      if (!(await verify(pw, before.salt, before.hash))) {
        this.fail(day, who, k);
        return reply({ ok: false, err: op === "save" ? "taken" : "wrong" });
      }
    } else if (op !== "save") {
      this.bump(day, who, "fail", now); // 없는 이름도 틀림으로 센다 (있는 이름 찾기 막기)
      return reply({ ok: false, err: "wrong" });
    } else {
      salt = randHex(16);
      hash = await pbkdf2(pw, salt);
    }

    // 여기부터는 기다림 없이 한 번에 처리한다 (중간에 바뀐 게 있으면 그만둔다)
    const t = Date.now();
    const cur = this.one("SELECT * FROM cfg WHERE k = ? AND expires > ?", k, t);
    if ((cur ? cur.hash : null) !== (before ? before.hash : null)) return reply({ ok: false, err: op === "save" ? "taken" : "wrong" });
    const again = this.full(day)[kind] ? { ok: false, err: "busy" } : this.gate(day, who, kind, t);
    if (again) return reply(again);
    const expires = t + CFG.keepDays * 86400000;
    if (op === "save") {
      if (cur) this.run("UPDATE cfg SET data = ?, expires = ?, fails = 0, lock_until = 0 WHERE k = ?", data, expires, k);
      else {
        this.run("DELETE FROM cfg WHERE k = ?", k);
        this.run("INSERT INTO cfg(k, salt, hash, data, expires, fails, lock_until) VALUES (?, ?, ?, ?, ?, 0, 0)", k, salt, hash, data, expires);
      }
    } else if (op === "extend") this.run("UPDATE cfg SET expires = ?, fails = 0 WHERE k = ?", expires, k);
    else this.run("UPDATE cfg SET fails = 0 WHERE k = ?", k);
    this.bump(day, who, kind, t);
    this.run("INSERT INTO totals(day, kind, n) VALUES (?, ?, 1) ON CONFLICT(day, kind) DO UPDATE SET n = n + 1", day, kind);
    return reply(op === "load" ? { ok: true, data: cur.data, expires: cur.expires } : { ok: true, expires });
  }

  /** IP를 그날의 비밀값으로 해시한다. 원본 IP는 어디에도 남기지 않는다 */
  async who(ip, day) {
    this.run("INSERT OR IGNORE INTO salts(day, v) VALUES (?, ?)", day, randHex(32));
    const salt = this.one("SELECT v FROM salts WHERE day = ?", day).v;
    const h = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(salt + "|" + ip));
    return toHex(new Uint8Array(h)).slice(0, 32);
  }

  count(day, who, kind) {
    const r = this.one("SELECT n FROM quota WHERE day = ? AND who = ? AND kind = ?", day, who, kind);
    return r ? r.n : 0;
  }

  /** 오늘 더 할 수 있는지: 막히면 {err, sec?}, 되면 null */
  gate(day, who, kind, now) {
    const r = this.one("SELECT n, last FROM quota WHERE day = ? AND who = ? AND kind = ?", day, who, kind);
    if (!r) return null;
    if (r.n >= CFG.perDay) return { ok: false, err: "daily" };
    const sec = Math.ceil((r.last + CFG.waitSec * 1000 - now) / 1000);
    return sec > 0 ? { ok: false, err: "wait", sec } : null;
  }

  quota(day, who, now) {
    const one = (kind) => {
      const r = this.one("SELECT n, last FROM quota WHERE day = ? AND who = ? AND kind = ?", day, who, kind);
      if (!r) return { left: CFG.perDay, wait: 0 };
      return { left: Math.max(0, CFG.perDay - r.n), wait: Math.max(0, Math.ceil((r.last + CFG.waitSec * 1000 - now) / 1000)) };
    };
    return { save: one("save"), load: one("load") };
  }

  full(day) {
    const n = (kind) => (this.one("SELECT n FROM totals WHERE day = ? AND kind = ?", day, kind) || { n: 0 }).n;
    return { save: n("save") >= CFG.saveCap, load: n("load") >= CFG.loadCap };
  }

  bump(day, who, kind, now) {
    this.run("INSERT INTO quota(day, who, kind, n, last) VALUES (?, ?, ?, 1, ?) ON CONFLICT(day, who, kind) DO UPDATE SET n = n + 1, last = excluded.last",
      day, who, kind, now);
  }

  /** 비밀번호 틀림: PC 쪽 틀림 수를 올리고, 같은 이름을 여러 번 틀리면 잠깐 잠근다 */
  fail(day, who, k) {
    const now = Date.now();
    this.bump(day, who, "fail", now);
    this.run("UPDATE cfg SET fails = fails + 1 WHERE k = ?", k);
    this.run("UPDATE cfg SET fails = 0, lock_until = ? WHERE k = ? AND fails >= ?", now + CFG.nameLockSec * 1000, k, CFG.nameFails);
  }

  /** 하루 한 번: 30일 지난 설정, 지난 기록, 지난 비밀값을 지운다 */
  async alarm() {
    const now = Date.now(), old = kstDay(now - 2 * 86400000);
    this.run("DELETE FROM cfg WHERE expires <= ?", now);
    this.run("DELETE FROM quota WHERE day < ?", old);
    this.run("DELETE FROM totals WHERE day < ?", old);
    this.run("DELETE FROM salts WHERE day < ?", kstDay(now));
    await this.state.storage.setAlarm(now + 86400000);
  }
}

function cleanName(n) {
  if (typeof n !== "string") return "";
  const s = n.normalize("NFC").trim();
  if (s.length < 1 || s.length > 20 || /[\u0000-\u001f\u007f]/.test(s)) return "";
  return s;
}

function randHex(bytes) {
  return toHex(crypto.getRandomValues(new Uint8Array(bytes)));
}

function toHex(a) {
  return [...a].map((x) => x.toString(16).padStart(2, "0")).join("");
}

function fromHex(h) {
  const a = new Uint8Array(h.length / 2);
  for (let i = 0; i < a.length; i++) a[i] = parseInt(h.substr(i * 2, 2), 16);
  return a;
}

async function pbkdf2(pw, saltHex) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(pw), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits({ name: "PBKDF2", hash: "SHA-256", salt: fromHex(saltHex), iterations: CFG.iter }, key, 256);
  return toHex(new Uint8Array(bits));
}

async function verify(pw, saltHex, hash) {
  const h = await pbkdf2(pw, saltHex);
  if (h.length !== hash.length) return false;
  let d = 0;
  for (let i = 0; i < h.length; i++) d |= h.charCodeAt(i) ^ hash.charCodeAt(i);
  return d === 0;
}

function kstDay(ms) {
  return new Date(ms + 9 * 3600 * 1000).toISOString().slice(0, 10);
}
