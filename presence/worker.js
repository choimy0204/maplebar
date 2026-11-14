// 메이플 바 하루 사용자 세기 (Cloudflare Worker + Durable Object)
// POST /hit    → 오늘(한국 시간) 수를 1 올린다. 바는 하루에 한 번만 보낸다
// GET  /count  헤더 X-Key: 비밀 열쇠 → {"today":오늘 수, "days":[{"day":"YYYY-MM-DD","n":수}, ...] (최근 30일, 최신 먼저),
//              "hoursToday":[24칸, 오늘 몇 시에 켰는지], "hours":[24칸, 최근 30일 합]}
// 비밀 열쇠는 코드에 넣지 않고 `npx wrangler secret put ADMIN_KEY`로 넣는다.

const KEEP = 30; // 돌려줄 날 수

export default {
  async fetch(req, env) {
    const p = new URL(req.url).pathname;
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
}

function kstDay(ms) {
  return new Date(ms + 9 * 3600 * 1000).toISOString().slice(0, 10);
}
