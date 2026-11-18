// 파티 방 시험: node party-test.mjs [http://127.0.0.1:8787]
const base = process.argv[2] || "http://127.0.0.1:8787";
const wsBase = base.replace(/^http/, "ws");
let fails = 0;
const ok = (name, cond, extra = "") => {
  console.log((cond ? "통과 " : "실패 ") + name + (cond ? "" : " " + extra));
  if (!cond) fails++;
};
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const post = async (path, body) => (await fetch(base + path, { method: "POST", body: JSON.stringify(body) })).json();

// 소켓을 열고 받은 메시지를 모아 둔다
function open(room, ticket) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(`${wsBase}/party/ws?room=${room}&ticket=${ticket}`);
    const c = { ws, msgs: [], closed: false };
    ws.onmessage = (e) => c.msgs.push(JSON.parse(e.data));
    ws.onclose = () => (c.closed = true);
    ws.onopen = () => resolve(c);
    ws.onerror = () => reject(new Error("소켓 오류"));
    c.send = (o) => ws.send(JSON.stringify(o));
    c.last = (t) => [...c.msgs].reverse().find((m) => m.t === t);
  });
}

// 붙기에 실패하는지 (403이면 소켓이 열리지 않는다)
const refused = (room, ticket) =>
  new Promise((r) => {
    const ws = new WebSocket(`${wsBase}/party/ws?room=${room}&ticket=${ticket}`);
    ws.onopen = () => { ws.close(); r(false); };
    ws.onerror = () => r(true);
  });

const a = await post("/party/create", { pw: "abcd1234" });
ok("만들기", a.ok && /^[A-HJ-NP-Z2-9]{6}$/.test(a.code) && a.room.length === 32 && a.ticket, JSON.stringify(a));

ok("틀린 비밀번호는 wrong", (await post("/party/join", { code: a.code, pw: "zzzzzz" })).err === "wrong");
ok("없는 코드는 gone", (await post("/party/join", { code: "AAAAAA", pw: "abcd1234" })).err === "gone");
ok("형식이 틀리면 bad", (await post("/party/join", { code: "ab", pw: "abcd1234" })).err === "bad");
ok("본문이 1KB 넘으면 bad", (await post("/party/join", { code: a.code, pw: "x".repeat(2000) })).err === "bad");
const b = await post("/party/join", { code: " " + a.code.toLowerCase() + " ", pw: "abcd1234" });
ok("맞는 비밀번호는 ok (소문자·공백 허용)", b.ok && b.room === a.room && b.ticket, JSON.stringify(b));

const ca = await open(a.room, a.ticket);
const cb = await open(b.room, b.ticket);
ca.send({ t: "hello", job: "아크메이지" });
cb.send({ t: "hello", job: "보우마스터" });
await sleep(400);
const mem = cb.last("members");
ok("welcome", ca.last("welcome")?.owner === true && cb.last("welcome")?.owner === false);
ok("members 2명 (들어온 순서, 방장 먼저)", mem?.list.length === 2 && mem.list[0].owner && mem.list[0].job === "아크메이지" && mem.code === a.code, JSON.stringify(mem));

ca.send({ t: "state", s: { job: "x", origin: 100 }, use: "origin" });
await sleep(300);
const sb = cb.last("state");
ok("A의 state를 B가 받음", sb?.use === "origin" && sb.s.origin === 100 && sb.age === 0, JSON.stringify(sb));
ok("보낸 사람은 자기 state를 못 받음", !ca.last("state"));
cb.send({ t: "state", s: { job: "y", burst: 50 }, use: "burst" });
await sleep(300);
ok("B의 state를 A가 받음", ca.last("state")?.s.burst === 50);

await sleep(1200);
const c = await post("/party/join", { code: a.code, pw: "abcd1234" });
const cc = await open(c.room, c.ticket);
cc.send({ t: "hello", job: "나이트로드" });
await sleep(400);
const stored = cc.msgs.filter((m) => m.t === "state");
ok("나중에 붙은 C가 저장된 state(age 포함)를 받음", stored.length === 2 && stored.every((m) => m.age >= 1000), JSON.stringify(stored));

// 같은 ticket 재접속 → 예전 소켓 닫힘
const cb2 = await open(b.room, b.ticket);
await sleep(300);
ok("같은 ticket 재접속 시 예전 소켓 닫힘", cb.closed);
cb2.send({ t: "hello", job: "보우마스터" });

// ping, 방장 아닌 사람의 close
cc.send({ t: "ping" });
cc.send({ t: "close" });
await sleep(300);
ok("pong", !!cc.last("pong"));
ok("방장이 아니면 close 거절", cc.last("err")?.err === "bad" && ca.last("members").code === a.code);

ca.send({ t: "close" });
await sleep(400);
ok("close 뒤 모두에게 members(code:null)", cc.last("members")?.code === null && cb2.last("members")?.code === null);
ok("close 뒤 같은 코드 입장은 gone", (await post("/party/join", { code: a.code, pw: "abcd1234" })).err === "gone");
ok("이미 들어온 사람은 그대로", !ca.closed && !cc.closed && (await fetch(`${base}/party/ws?room=${a.room}&ticket=${a.ticket}`)).status === 426);

// 4KB 넘는 메시지는 버림
const before = cc.msgs.length;
ca.send({ t: "state", s: { pad: "x".repeat(5000) }, use: null });
await sleep(300);
ok("4KB 넘는 메시지는 버림", cc.msgs.length === before);

// 틀린 ticket은 403
ok("틀린 ticket은 거절(403)", await refused(a.room, "0".repeat(32)));

// leave
cc.send({ t: "leave" });
await sleep(400);
ok("leave 후 소켓 닫힘, 남은 사람에게 members 2명", cc.closed && ca.last("members").list.length === 2);

// 방장 넘기기: A(방장)가 나가면 먼저 들어온 B가 방장
ca.send({ t: "leave" });
await sleep(400);
const hand = cb2.last("members");
ok("방장이 나가면 다음 사람(B)이 방장", ca.closed && hand?.list.length === 1 && hand.list[0].owner === true && hand.list[0].id === cb2.last("welcome")?.id, JSON.stringify(hand));
cb2.send({ t: "disband" });
await sleep(400);
ok("새 방장의 disband가 통함", cb2.last("bye")?.why === "disband" && cb2.closed);

// 방장 넘기기 2: 3명 중 방장이 나가면 두 번째로 들어온 사람, 그 사람이 close 가능
const h = await post("/party/create", { pw: "hand1234" });
const h2 = await post("/party/join", { code: h.code, pw: "hand1234" });
const h3 = await post("/party/join", { code: h.code, pw: "hand1234" });
const o1 = await open(h.room, h.ticket);
const o2 = await open(h2.room, h2.ticket);
const o3 = await open(h3.room, h3.ticket);
for (const x of [o1, o2, o3]) x.send({ t: "hello", job: "j" });
await sleep(400);
o1.send({ t: "leave" });
await sleep(400);
const m3 = o3.last("members");
const id2 = o2.last("welcome").id;
ok("3명 중 방장 퇴장: 두 번째 사람이 방장, 목록 2명", m3.list.length === 2 && m3.list.find((x) => x.owner)?.id === id2 && m3.list.filter((x) => x.owner).length === 1, JSON.stringify(m3));
o3.send({ t: "close" });
await sleep(300);
ok("방장 아닌 사람(C)의 close는 여전히 거절", o3.last("err")?.err === "bad");
o2.send({ t: "close" });
await sleep(400);
ok("새 방장의 close가 통함 (code:null)", o3.last("members")?.code === null && o2.last("members")?.code === null);
o2.send({ t: "disband" });
await sleep(400);
ok("새 방장의 disband가 통함", o3.last("bye")?.why === "disband");

// 새 방: 7명째 full, disband
const r = await post("/party/create", { pw: "room2pass" });
const members = [r];
for (let i = 0; i < 5; i++) members.push(await post("/party/join", { code: r.code, pw: "room2pass" }));
ok("6명까지 입장", members.every((m) => m.ok));
ok("7명째는 full", (await post("/party/join", { code: r.code, pw: "room2pass" })).err === "full");
const o = await open(r.room, r.ticket);
const p2 = await open(r.room, members[1].ticket);
o.send({ t: "hello", job: "a" });
p2.send({ t: "hello", job: "b" });
await sleep(300);
p2.send({ t: "disband" });
await sleep(300);
ok("방장 아닌 사람의 disband 거절", !!p2.last("err") && !o.closed);
o.send({ t: "disband" });
await sleep(500);
ok("disband 시 bye 받고 닫힘", o.last("bye")?.why === "disband" && p2.last("bye")?.why === "disband" && o.closed && p2.closed);
ok("disband 뒤 입장은 gone", (await post("/party/join", { code: r.code, pw: "room2pass" })).err === "gone");
ok("disband 뒤 ticket은 거절(403)", await refused(r.room, r.ticket));

// 소켓 속도 제한
const r3 = await post("/party/create", { pw: "room3pass" });
const s3 = await open(r3.room, r3.ticket);
for (let i = 0; i < 40; i++) s3.send({ t: "ping" });
await sleep(500);
ok("10초 30개 넘으면 rate 후 닫힘", s3.last("err")?.err === "rate" && s3.closed);
const dis = await open(r3.room, r3.ticket);
dis.send({ t: "disband" });
await sleep(300);

// 기존 경로
ok("/hit 은 404 아님", (await fetch(base + "/hit", { method: "POST" })).status === 200);
ok("/count 는 열쇠 없이 403", (await fetch(base + "/count")).status === 403);
ok("/cfg/status 동작", (await post("/cfg/status", {})).ok === true);
ok("모르는 경로는 404", (await fetch(base + "/nope")).status === 404);
ok("/party/x 는 404", (await fetch(base + "/party/x")).status === 404);

for (const x of [ca, cb2, cc]) { try { x.ws.close(); } catch { } }
console.log(fails === 0 ? "\n모두 통과" : `\n실패 ${fails}개`);
process.exit(fails === 0 ? 0 : 1);
