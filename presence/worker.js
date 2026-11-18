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
//
// 파티 방 (Durable Object 클래스 Party. 인스턴스 "lobby"는 코드 표, 방마다 인스턴스 하나)
// POST /party/create {pw}        → {ok, code, room, ticket}
// POST /party/join   {code, pw}  → {ok, room, ticket}
// 실패하면 {ok:false, err}. err: bad·wrong·gone·full·tries·busy
// GET  /party/ws?room=&ticket=   WebSocket. 메시지는 JSON 한 줄 (hello·state·close·disband·leave·ping)
// 비밀번호는 해시로만 저장하고, 코드는 12시간 뒤, 빈 방은 15분 뒤 지운다.

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
    if (p.startsWith("/party/")) return partyRoute(req, env, p);
    if (p === "/ask" || p.startsWith("/ask/")) return askRoute(req, env, p);
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
    if (p.startsWith("/ask/")) return this.ask(req, p.slice(5));
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
      sql.exec("CREATE TABLE IF NOT EXISTS ask(id INTEGER PRIMARY KEY AUTOINCREMENT, at INTEGER, kind TEXT, body TEXT, reply TEXT, read INTEGER DEFAULT 0)");
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
    this.run("DELETE FROM ask WHERE at <= ?", now - ASK.keepDays * 86400000);
    await this.state.storage.setAlarm(now + 86400000);
  }

  // ── 1:1 문의 ("ask" 인스턴스만 쓴다) ──
  async ask(req, op) {
    const now = Date.now(), day = kstDay(now);
    if (!this.alarmSet) {
      if ((await this.state.storage.getAlarm()) == null) await this.state.storage.setAlarm(now + 86400000);
      this.alarmSet = true;
    }
    let b;
    try { b = JSON.parse((await req.text()) || "{}"); } catch { return Response.json({ ok: false, err: "bad" }); }
    if (!b || typeof b !== "object" || Array.isArray(b)) return Response.json({ ok: false, err: "bad" });

    if (op === "send") {
      // 봇으로 보이면 저장하지 않고 성공한 척한다 (숨은 칸을 채웠거나, 너무 빨리 보냈거나, 링크가 많을 때)
      if (b.site || !(Number(b.ms) >= ASK.minMs)) return Response.json({ ok: true });
      const kind = ["bug", "idea", "etc"].includes(b.kind) ? b.kind : "etc";
      const body = cleanText(b.body, ASK.bodyMax), reply = cleanText(b.reply, ASK.replyMax).replace(/\s+/g, " ");
      if (body === null || reply === null) return Response.json({ ok: false, err: "long" });
      if (body.length < 5) return Response.json({ ok: false, err: "short" });
      if ((body.match(/https?:\/\//gi) || []).length > 3) return Response.json({ ok: true });

      const who = await this.who(req.headers.get("X-Ip") || "", day);
      const total = (this.one("SELECT n FROM totals WHERE day = ? AND kind = 'ask'", day) || { n: 0 }).n;
      if (total >= ASK.dayCap) return Response.json({ ok: false, err: "full" });
      const r = this.one("SELECT n, last FROM quota WHERE day = ? AND who = ? AND kind = 'ask'", day, who);
      if (r && r.n >= ASK.perDay) return Response.json({ ok: false, err: "daily" });
      if (r && now - r.last < ASK.waitSec * 1000) return Response.json({ ok: false, err: "fast" });

      this.run("INSERT INTO ask(at, kind, body, reply, read) VALUES (?, ?, ?, ?, 0)", now, kind, body, reply);
      this.bump(day, who, "ask", now);
      this.run("INSERT INTO totals(day, kind, n) VALUES (?, 'ask', 1) ON CONFLICT(day, kind) DO UPDATE SET n = n + 1", day);
      return Response.json({ ok: true });
    }

    // 여기부터는 개발자만 (비밀 열쇠)
    if (!sameKey(req.headers.get("X-Key") || "", this.env.ADMIN_KEY)) return Response.json({ ok: false, err: "key" }, { status: 403 });
    const id = Number(b.id) | 0;
    if (op === "read") this.run("UPDATE ask SET read = ? WHERE id = ?", b.unread ? 0 : 1, id);
    else if (op === "del") this.run("DELETE FROM ask WHERE id = ?", id);
    else if (op !== "list") return Response.json({ ok: false, err: "bad" });
    this.run("DELETE FROM ask WHERE at <= ?", now - ASK.keepDays * 86400000);
    const items = this.db().exec("SELECT id, at, kind, body, reply, read FROM ask ORDER BY id DESC LIMIT 300").toArray();
    return Response.json({ ok: true, items, keepDays: ASK.keepDays });
  }
}

// ── 파티 방 ──
const PARTY = {
  bodyMax: 1024,                 // 만들기·입장 본문 한도
  msgMax: 4096,                  // 소켓 메시지 한도
  maxMembers: 6,
  codeKeepMs: 12 * 3600 * 1000,  // 코드 보관
  emptyMs: 15 * 60 * 1000,       // 빈 방을 지우기까지
  makePerHour: 10,               // 같은 IP 만들기 1시간
  failPer10Min: 10,              // 같은 IP 입장 실패 10분
  codeFails: 20,                 // 같은 코드 실패 이만큼이면 잠금
  ratePer10s: 30,                // 소켓당 10초 메시지
  alphabet: "ABCDEFGHJKLMNPQRSTUVWXYZ23456789",
};

async function partyRoute(req, env, p) {
  if (req.method === "POST" && (p === "/party/create" || p === "/party/join")) {
    const len = Number(req.headers.get("Content-Length") || 0);
    if (len > PARTY.bodyMax) return Response.json({ ok: false, err: "bad" }, { status: 413 });
    const body = await req.text();
    if (body.length > PARTY.bodyMax) return Response.json({ ok: false, err: "bad" }, { status: 413 });
    // 바깥 머리글은 버리고 IP만 넘긴다
    const inner = new Request("https://party" + p.slice(6), {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Ip": req.headers.get("CF-Connecting-IP") || "" },
      body,
    });
    return env.PARTY.get(env.PARTY.idFromName("lobby")).fetch(inner);
  }
  if (req.method === "GET" && p === "/party/ws") {
    const room = new URL(req.url).searchParams.get("room") || "";
    if (!/^[0-9a-f]{32}$/.test(room)) return new Response("forbidden", { status: 403 });
    return env.PARTY.get(env.PARTY.idFromName(room)).fetch(req);
  }
  return new Response("not found", { status: 404 });
}

export class Party {
  constructor(state, env) {
    this.state = state;
    this.env = env;
  }

  async fetch(req) {
    const p = new URL(req.url).pathname;
    if (p === "/create" || p === "/join") return this.lobby(req, p.slice(1));
    if (p === "/drop") return this.drop(req);
    if (p === "/r/init") return this.roomInit(req);
    if (p === "/r/join") return this.roomJoin();
    if (p === "/r/expire") return this.roomExpire();
    if (p === "/party/ws") return this.roomSocket(req);
    return new Response("not found", { status: 404 });
  }

  async alarm() {
    const kind = await this.state.storage.get("kind");
    if (kind === "lobby") return this.lobbyAlarm();
    if (kind === "room") return this.roomAlarm();
  }

  // ── 로비 ("lobby" 인스턴스) ──

  db() {
    const sql = this.state.storage.sql;
    if (!this.ready) {
      sql.exec("CREATE TABLE IF NOT EXISTS codes(code TEXT PRIMARY KEY, room TEXT, salt TEXT, hash TEXT, created INTEGER, fails INTEGER DEFAULT 0)");
      sql.exec("CREATE TABLE IF NOT EXISTS ev(who TEXT, kind TEXT, at INTEGER)");
      sql.exec("CREATE TABLE IF NOT EXISTS salts(day TEXT PRIMARY KEY, v TEXT)");
      this.ready = true;
    }
    return sql;
  }
  one(q, ...a) { return this.db().exec(q, ...a).toArray()[0]; }
  run(q, ...a) { this.db().exec(q, ...a); }

  /** IP를 그날의 비밀값으로 해시한다. 원본 IP는 남기지 않는다 */
  async who(ip, day) {
    this.run("INSERT OR IGNORE INTO salts(day, v) VALUES (?, ?)", day, randHex(32));
    const salt = this.one("SELECT v FROM salts WHERE day = ?", day).v;
    const h = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(salt + "|" + ip));
    return toHex(new Uint8Array(h)).slice(0, 32);
  }

  recent(who, kind, ms, now) {
    return this.one("SELECT COUNT(*) AS n FROM ev WHERE who = ? AND kind = ? AND at > ?", who, kind, now - ms).n;
  }

  /** 오래된 코드·기록을 지운다. 지운 코드의 방에는 모집이 끝났다고 알린다 */
  async cleanup(now) {
    const old = this.db().exec("SELECT code, room FROM codes WHERE created <= ?", now - PARTY.codeKeepMs).toArray();
    this.run("DELETE FROM codes WHERE created <= ?", now - PARTY.codeKeepMs);
    this.run("DELETE FROM ev WHERE at <= ?", now - 3600 * 1000);
    this.run("DELETE FROM salts WHERE day < ?", kstDay(now));
    for (const r of old) {
      try { await this.roomStub(r.room).fetch(new Request("https://party/r/expire", { method: "POST" })); } catch { }
    }
  }

  roomStub(room) {
    return this.env.PARTY.get(this.env.PARTY.idFromName(room));
  }

  async ensureLobbyAlarm(now) {
    await this.state.storage.put("kind", "lobby");
    if ((await this.state.storage.getAlarm()) == null) await this.state.storage.setAlarm(now + 3600 * 1000);
  }

  async lobbyAlarm() {
    const now = Date.now();
    await this.cleanup(now);
    if (this.one("SELECT COUNT(*) AS n FROM codes").n > 0) await this.state.storage.setAlarm(now + 3600 * 1000);
  }

  async lobby(req, op) {
    let b;
    try { b = JSON.parse((await req.text()) || "{}"); } catch { return Response.json({ ok: false, err: "bad" }); }
    if (!b || typeof b !== "object" || Array.isArray(b)) return Response.json({ ok: false, err: "bad" });
    const pw = typeof b.pw === "string" ? b.pw : "";
    if (pw.length < 4 || pw.length > 20) return Response.json({ ok: false, err: "bad" });

    const now = Date.now();
    await this.ensureLobbyAlarm(now);
    await this.cleanup(now);
    const who = await this.who(req.headers.get("X-Ip") || "", kstDay(now));

    if (op === "create") {
      if (this.recent(who, "mk", 3600 * 1000, now) >= PARTY.makePerHour) return Response.json({ ok: false, err: "busy" });
      this.run("INSERT INTO ev(who, kind, at) VALUES (?, 'mk', ?)", who, now);
      const room = randHex(16);
      const salt = randHex(16);
      const hash = await pbkdf2(pw, salt);
      let code = "";
      for (let i = 0; i < 20; i++) {
        const c = newCode();
        if (!this.one("SELECT 1 AS x FROM codes WHERE code = ?", c)) { code = c; break; }
      }
      if (!code) return Response.json({ ok: false, err: "busy" });
      // 코드를 먼저 잡아 두고(다른 요청이 같은 코드를 못 쓰게) 방을 만든다
      this.run("INSERT INTO codes(code, room, salt, hash, created, fails) VALUES (?, ?, ?, ?, ?, 0)", code, room, salt, hash, now);
      const res = await this.roomStub(room).fetch(new Request("https://party/r/init", {
        method: "POST", body: JSON.stringify({ code, room }),
      }));
      const r = await res.json();
      if (!r.ok) {
        this.run("DELETE FROM codes WHERE code = ?", code);
        return Response.json({ ok: false, err: "busy" });
      }
      return Response.json({ ok: true, code, room, ticket: r.ticket });
    }

    // 입장
    const code = typeof b.code === "string" ? b.code.replace(/\s+/g, "").toUpperCase() : "";
    if (!/^[A-HJ-NP-Z2-9]{6}$/.test(code)) return Response.json({ ok: false, err: "bad" });
    if (this.recent(who, "fail", 10 * 60 * 1000, now) >= PARTY.failPer10Min) return Response.json({ ok: false, err: "tries" });
    const row = this.one("SELECT * FROM codes WHERE code = ?", code);
    if (!row) {
      this.run("INSERT INTO ev(who, kind, at) VALUES (?, 'fail', ?)", who, now); // 없는 코드도 실패로 센다
      return Response.json({ ok: false, err: "gone" });
    }
    if (row.fails >= PARTY.codeFails) return Response.json({ ok: false, err: "tries" });
    if (!(await verify(pw, row.salt, row.hash))) {
      this.run("INSERT INTO ev(who, kind, at) VALUES (?, 'fail', ?)", who, Date.now());
      this.run("UPDATE codes SET fails = fails + 1 WHERE code = ?", code);
      return Response.json({ ok: false, err: "wrong" });
    }
    const res = await this.roomStub(row.room).fetch(new Request("https://party/r/join", { method: "POST" }));
    const r = await res.json();
    if (!r.ok) {
      if (r.err === "gone") this.run("DELETE FROM codes WHERE code = ?", code);
      return Response.json({ ok: false, err: r.err });
    }
    return Response.json({ ok: true, room: row.room, ticket: r.ticket });
  }

  async drop(req) {
    let b;
    try { b = await req.json(); } catch { return new Response("bad", { status: 400 }); }
    this.run("DELETE FROM codes WHERE code = ? AND room = ?", String(b.code), String(b.room));
    return new Response("ok");
  }

  // ── 방 (방마다 인스턴스) ──

  async tickets() {
    return (await this.state.storage.get("tk")) || [];
  }

  async loadRoom() {
    if (!this.room) this.room = (await this.state.storage.get("room")) || "";
  }

  /** 로비에서 이 방의 코드를 지운다 */
  async dropCode(code) {
    if (!code) return;
    await this.loadRoom();
    try {
      await this.env.PARTY.get(this.env.PARTY.idFromName("lobby")).fetch(new Request("https://party/drop", {
        method: "POST", body: JSON.stringify({ code, room: this.room }),
      }));
    } catch { }
  }

  async roomInit(req) {
    const b = await req.json();
    const owner = { id: randHex(4), ticket: randHex(16), owner: true, job: "", seq: 1 };
    this.room = b.room;
    await this.state.storage.put({ kind: "room", room: b.room, tk: [owner], code: b.code, seq: 1 });
    await this.state.storage.setAlarm(Date.now() + PARTY.emptyMs);
    return Response.json({ ok: true, ticket: owner.ticket });
  }

  async roomJoin() {
    const tk = await this.tickets();
    const code = (await this.state.storage.get("code")) || null;
    if (tk.length === 0 || !code) return Response.json({ ok: false, err: "gone" });
    if (tk.length >= PARTY.maxMembers) return Response.json({ ok: false, err: "full" });
    const seq = ((await this.state.storage.get("seq")) || 0) + 1;
    const m = { id: randHex(4), ticket: randHex(16), owner: false, job: "", seq };
    tk.push(m);
    await this.state.storage.put({ tk, seq });
    if ((await this.state.storage.getAlarm()) == null && this.state.getWebSockets().length === 0)
      await this.state.storage.setAlarm(Date.now() + PARTY.emptyMs);
    await this.sendMembers(tk);
    return Response.json({ ok: true, ticket: m.ticket });
  }

  /** 코드가 오래돼서 모집이 끝났다 */
  async roomExpire() {
    const tk = await this.tickets();
    if (tk.length === 0) return new Response("ok");
    await this.state.storage.put("code", null);
    await this.sendMembers(tk);
    return new Response("ok");
  }

  async roomAlarm() {
    if (this.state.getWebSockets().length > 0) return;
    const code = await this.state.storage.get("code");
    await this.loadRoom();
    await this.wipe();
    await this.dropCode(code);
  }

  /** 방 기록을 모두 지운다 */
  async wipe() {
    await this.state.storage.deleteAlarm();
    await this.state.storage.deleteAll();
  }

  async membersMsg(tk) {
    const code = (await this.state.storage.get("code")) || null;
    return JSON.stringify({
      t: "members",
      list: [...tk].sort((a, b) => a.seq - b.seq).map((m) => ({ id: m.id, job: m.job, owner: m.owner })),
      code,
    });
  }

  async sendMembers(tk) {
    const msg = await this.membersMsg(tk);
    for (const ws of this.state.getWebSockets()) send(ws, msg);
  }

  async roomSocket(req) {
    if (req.headers.get("Upgrade") !== "websocket") return new Response("upgrade required", { status: 426 });
    const ticket = new URL(req.url).searchParams.get("ticket") || "";
    const tk = await this.tickets();
    const me = tk.find((m) => sameKey(ticket, m.ticket));
    if (!me) return new Response("forbidden", { status: 403 });

    // 같은 ticket이 다시 붙으면 예전 소켓은 닫는다
    for (const old of this.state.getWebSockets(me.id)) {
      try { old.close(1000, "replaced"); } catch { }
    }
    const pair = new WebSocketPair();
    const [client, server] = Object.values(pair);
    this.state.acceptWebSocket(server, [me.id]);
    server.serializeAttachment({ id: me.id, w: Date.now(), n: 0 });
    await this.state.storage.deleteAlarm();
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws, data) {
    const at = ws.deserializeAttachment() || {};
    const now = Date.now();
    if (now - at.w > 10000) { at.w = now; at.n = 0; }
    at.n++;
    ws.serializeAttachment(at);
    if (at.n > PARTY.ratePer10s) {
      send(ws, JSON.stringify({ t: "err", err: "rate" }));
      try { ws.close(1008, "rate"); } catch { }
      return;
    }
    if (typeof data !== "string" || data.length > PARTY.msgMax) return;
    let m;
    try { m = JSON.parse(data); } catch { return; }
    if (!m || typeof m !== "object" || typeof m.t !== "string") return;

    const tk = await this.tickets();
    const me = tk.find((x) => x.id === at.id);
    if (!me) { try { ws.close(1000, "gone"); } catch { } return; }

    if (m.t === "ping") return send(ws, '{"t":"pong"}');

    if (m.t === "hello") {
      me.job = typeof m.job === "string" ? m.job.slice(0, 40) : "";
      await this.state.storage.put("tk", tk);
      send(ws, JSON.stringify({ t: "welcome", id: me.id, owner: me.owner }));
      await this.sendMembers(tk);
      const states = await this.state.storage.list({ prefix: "st:" });
      for (const [k, v] of states) {
        send(ws, JSON.stringify({ t: "state", id: k.slice(3), s: v.s, use: v.use, age: Math.max(0, now - v.at) }));
      }
      return;
    }

    if (m.t === "state") {
      if (!m.s || typeof m.s !== "object" || Array.isArray(m.s)) return send(ws, '{"t":"err","err":"bad"}');
      const use = ["origin", "burst", "semi", "ascent"].includes(m.use) ? m.use : null;
      await this.state.storage.put("st:" + me.id, { s: m.s, use, at: now });
      const out = JSON.stringify({ t: "state", id: me.id, s: m.s, use, age: 0 });
      for (const other of this.state.getWebSockets()) if (other !== ws) send(other, out);
      return;
    }

    if (m.t === "close" || m.t === "disband") {
      if (!me.owner) return send(ws, '{"t":"err","err":"bad"}');
      const code = await this.state.storage.get("code");
      await this.dropCode(code);
      if (m.t === "close") {
        await this.state.storage.put("code", null);
        await this.sendMembers(tk);
        return;
      }
      const socks = this.state.getWebSockets();
      for (const s of socks) send(s, '{"t":"bye","why":"disband"}');
      await this.wipe();
      for (const s of socks) { try { s.close(1000, "disband"); } catch { } }
      return;
    }

    if (m.t === "leave") {
      const rest = tk.filter((x) => x.id !== me.id);
      try { ws.close(1000, "leave"); } catch { }
      if (rest.length === 0) {
        const code = await this.state.storage.get("code");
        await this.wipe();
        await this.dropCode(code);
        return;
      }
      await this.state.storage.delete("st:" + me.id);
      if (me.owner) {
        // 방장이 나가면 남은 사람 중 가장 먼저 들어온 사람이 방장이 된다
        const next = rest.reduce((a, b) => (b.seq < a.seq ? b : a));
        next.owner = true;
      }
      await this.state.storage.put("tk", rest);
      await this.sendMembers(rest);
      return;
    }
  }

  async webSocketClose(ws) {
    try { ws.close(1000, "bye"); } catch { }
    await this.afterDisconnect(ws);
  }

  async webSocketError(ws) {
    await this.afterDisconnect(ws);
  }

  /** 연결된 소켓이 하나도 없으면 15분 뒤 방을 지운다 */
  async afterDisconnect(ws) {
    if ((await this.tickets()).length === 0) return;
    if (this.state.getWebSockets().some((s) => s !== ws)) return;
    await this.state.storage.setAlarm(Date.now() + PARTY.emptyMs);
  }
}

function newCode() {
  const b = crypto.getRandomValues(new Uint8Array(6));
  return [...b].map((x) => PARTY.alphabet[x % 32]).join("");
}

function send(ws, msg) {
  try { ws.send(msg); } catch { }
}

// ── 1:1 문의 ──
// POST /ask         {kind, body, reply, site, ms} → {ok} 또는 {ok:false, err: short·long·fast·daily·full·bad}
//                    사이트(choimy0204.github.io)에서만 받는다. site는 봇 거르는 숨은 칸, ms는 페이지를 연 뒤 보낸 데까지 걸린 시간
// GET  /ask/admin    개발자용 읽기 페이지 (비밀 열쇠 ADMIN_KEY를 넣어야 글이 보인다)
// POST /ask/list {}  ·  /ask/read {id, unread?}  ·  /ask/del {id}   헤더 X-Key 필요 → {ok, items, keepDays}
// 글과 답장 받을 곳만 저장하고 90일 뒤에 지운다. IP는 그날의 비밀값으로 해시해서 하루 횟수만 센다.
const ASK = {
  origins: ["https://choimy0204.github.io", "http://127.0.0.1:8765", "http://localhost:8765"],
  bodyMax: 1000, replyMax: 80, reqMax: 8 * 1024,
  minMs: 3000,      // 페이지를 연 뒤 이보다 빨리 보내면 봇으로 본다
  perDay: 3,        // 같은 IP 하루
  waitSec: 60,      // 한 번 보내고 다음까지
  dayCap: 300,      // 서버 전체 하루
  keepDays: 90,
};

async function askRoute(req, env, p) {
  const origin = req.headers.get("Origin") || "";
  const allowed = ASK.origins.includes(origin);
  const cors = allowed ? { "Access-Control-Allow-Origin": origin, "Vary": "Origin" } : {};
  if (p === "/ask") {
    if (req.method === "OPTIONS") {
      return new Response(null, { status: allowed ? 204 : 403, headers: { ...cors, "Access-Control-Allow-Methods": "POST", "Access-Control-Allow-Headers": "Content-Type", "Access-Control-Max-Age": "86400" } });
    }
    if (req.method !== "POST" || !allowed) return Response.json({ ok: false, err: "bad" }, { status: 403, headers: cors });
    const res = await askInner(req, env, "/ask/send");
    return new Response(res.body, { status: res.status, headers: { ...cors, "Content-Type": "application/json" } });
  }
  if (p === "/ask/admin" && req.method === "GET") {
    return new Response(ADMIN_HTML, { headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store", "X-Robots-Tag": "noindex", "X-Frame-Options": "DENY", "Referrer-Policy": "no-referrer" } });
  }
  if (req.method === "POST" && /^\/ask\/(list|read|del)$/.test(p)) {
    // 읽기 페이지와 같은 주소에서만 (다른 사이트가 열쇠를 들고 부르지 못하게)
    const self = new URL(req.url).origin;
    if (origin && origin !== self) return Response.json({ ok: false, err: "bad" }, { status: 403 });
    return askInner(req, env, p);
  }
  return new Response("not found", { status: 404 });
}

async function askInner(req, env, path) {
  const len = Number(req.headers.get("Content-Length") || 0);
  if (len > ASK.reqMax) return Response.json({ ok: false, err: "long" }, { status: 413 });
  const body = await req.text();
  if (body.length > ASK.reqMax) return Response.json({ ok: false, err: "long" }, { status: 413 });
  const inner = new Request("https://ask" + path, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Ip": req.headers.get("CF-Connecting-IP") || "", "X-Key": req.headers.get("X-Key") || "" },
    body,
  });
  return env.COUNTER.get(env.COUNTER.idFromName("ask")).fetch(inner);
}

/** 글 다듬기: 줄바꿈과 탭만 남기고 제어 문자를 지운다. 너무 길면 null */
function cleanText(s, max) {
  if (s == null || s === "") return "";
  if (typeof s !== "string") return null;
  const t = s.normalize("NFC").replace(/\r\n?/g, "\n").replace(/[\u0000-\u0008\u000b-\u001f\u007f]/g, "").trim();
  return t.length > max ? null : t;
}

function sameKey(a, b) {
  if (!b || typeof a !== "string" || a.length !== b.length) return false;
  let d = 0;
  for (let i = 0; i < a.length; i++) d |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return d === 0;
}

const ADMIN_HTML = `<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex"><title>메이플 바 문의함</title>
<style>
:root{--bg:#f5f3ef;--panel:#fff;--ink:#171a22;--muted:#5b6170;--line:#e4e0d9;--accent:#e8642a;--soft:#fdeadf;--err:#c43d32}
@media (prefers-color-scheme:dark){:root{--bg:#0f131c;--panel:#171c28;--ink:#eceef3;--muted:#a0a7b7;--line:#283043;--accent:#ff8a4c;--soft:#3a2418;--err:#ff8577;color-scheme:dark}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.65 "Pretendard Variable",Pretendard,"Malgun Gothic",sans-serif;word-break:keep-all;overflow-wrap:anywhere}
.wrap{max-width:760px;margin:0 auto;padding:28px 16px 60px;display:grid;gap:16px}
h1{margin:0;font-size:24px}.muted{color:var(--muted);font-size:13px;margin:0}
.row{display:flex;gap:8px;flex-wrap:wrap;align-items:center}
input{flex:1;min-width:0;font:inherit;color:var(--ink);background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:9px 12px}
button{font:inherit;font-weight:700;font-size:14px;border-radius:8px;border:1px solid var(--line);background:var(--panel);color:var(--ink);padding:8px 14px;cursor:pointer}
button.main{background:var(--accent);border-color:var(--accent);color:#fff}
.tabs button.on{border-color:var(--accent);background:var(--soft)}
.item{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:16px 18px;display:grid;gap:8px}
.item.new{border-color:var(--accent)}
.meta{display:flex;gap:8px;flex-wrap:wrap;align-items:center;font-size:13px;color:var(--muted)}
.k{font-weight:700;font-size:12px;padding:2px 8px;border-radius:6px;background:var(--soft);color:var(--accent)}
.body{white-space:pre-wrap;margin:0}.reply{font-size:14px;margin:0}.reply b{color:var(--muted);font-weight:600}
.err{color:var(--err);font-weight:600}
</style></head><body><div class="wrap">
<h1>메이플 바 문의함</h1>
<div class="row" id="login"><input id="key" type="password" placeholder="비밀 열쇠" autocomplete="current-password"><button class="main" id="go">열기</button></div>
<p class="muted" id="info"></p>
<div class="row tabs" id="tabs" hidden><button data-f="new" class="on">안 읽은 글</button><button data-f="all">모든 글</button><button id="re">새로 고침</button></div>
<div id="list" style="display:grid;gap:12px"></div>
</div>
<script>
var KIND={bug:"안 돼요",idea:"기능 제안",etc:"그 밖의 것"},key="",filter="new",items=[];
try{key=sessionStorage.getItem("k")||""}catch(e){}
var $=function(id){return document.getElementById(id)};
function call(op,data){return fetch("/ask/"+op,{method:"POST",headers:{"Content-Type":"application/json","X-Key":key},body:JSON.stringify(data||{})}).then(function(r){return r.json()})}
function load(op,data){
  call(op,data).then(function(j){
    if(!j.ok){$("info").innerHTML='<span class="err">'+(j.err==="key"?"비밀 열쇠가 맞지 않아요.":"불러오지 못했어요.")+'</span>';$("login").hidden=false;$("tabs").hidden=true;return}
    try{sessionStorage.setItem("k",key)}catch(e){}
    $("login").hidden=true;$("tabs").hidden=false;items=j.items;
    var n=items.filter(function(x){return !x.read}).length;
    $("info").textContent="안 읽은 글 "+n+"개 · 모두 "+items.length+"개 · 글은 "+j.keepDays+"일 뒤에 지워져요";
    draw();
  }).catch(function(){$("info").innerHTML='<span class="err">불러오지 못했어요.</span>'});
}
function draw(){
  var L=$("list");L.textContent="";
  var show=items.filter(function(x){return filter==="all"||!x.read});
  if(!show.length){var p=document.createElement("p");p.className="muted";p.textContent=filter==="all"?"받은 글이 없어요.":"안 읽은 글이 없어요.";L.appendChild(p);return}
  show.forEach(function(x){
    var d=document.createElement("div");d.className="item"+(x.read?"":" new");
    var m=document.createElement("div");m.className="meta";
    var k=document.createElement("span");k.className="k";k.textContent=KIND[x.kind]||x.kind;
    var t=document.createElement("span");t.textContent=new Date(x.at).toLocaleString("ko-KR");
    m.appendChild(k);m.appendChild(t);d.appendChild(m);
    var b=document.createElement("p");b.className="body";b.textContent=x.body;d.appendChild(b);
    var r=document.createElement("p");r.className="reply";r.innerHTML="<b>답장 받을 곳</b> ";r.appendChild(document.createTextNode(x.reply||"없음"));d.appendChild(r);
    var row=document.createElement("div");row.className="row";
    var rd=document.createElement("button");rd.textContent=x.read?"안 읽음으로":"읽음";rd.onclick=function(){load("read",{id:x.id,unread:!!x.read})};
    var del=document.createElement("button");del.textContent="지우기";del.onclick=function(){if(confirm("이 글을 지울까요? 되돌릴 수 없어요."))load("del",{id:x.id})};
    row.appendChild(rd);row.appendChild(del);d.appendChild(row);L.appendChild(d);
  });
}
$("go").onclick=function(){key=$("key").value.trim();if(key)load("list")};
$("key").onkeydown=function(e){if(e.key==="Enter")$("go").click()};
document.querySelectorAll("#tabs [data-f]").forEach(function(b){b.onclick=function(){filter=b.dataset.f;document.querySelectorAll("#tabs [data-f]").forEach(function(o){o.classList.toggle("on",o===b)});draw()}});
$("re").onclick=function(){load("list")};
if(key)load("list");
</script></body></html>`;

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
