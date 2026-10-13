const copy = document.querySelector('#copy-command');
copy.hidden = false;
copy.addEventListener('click', async () => {
  const command = document.querySelector('#install-command');
  const status = document.querySelector('#copy-status');
  try {
    await navigator.clipboard.writeText(command.textContent);
    status.textContent = 'Command copied.';
  } catch {
    const range = document.createRange();
    range.selectNodeContents(command);
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    status.textContent = 'Copy unavailable. The command is selected; use your device’s copy command.';
  }
});


// Walkthrough: one cursor plays the three scenes in turn and loops, so it reads as one person doing it:
// 1) right-clicks weather-app in Explorer, runs down the menu and clicks Create dump with DumpToTxt;
// 2) the review window scans, the cursor unticks cities.json (sizes and shares update) and clicks Create dump;
// 3) Notepad opens the new dump and the cursor scrolls through it, then leaves.
// Targets are measured just before each scene starts; the scene's own changes are timed off the same clock.
const wt = document.querySelector('#walkthrough');
const items = [...wt.querySelectorAll('.steps > li')];
const scenes = items.map(li => li.querySelector('.scene'));
const pristine = scenes.map(s => [s.className, s.innerHTML]);
const layer = wt.querySelector('.cursor-layer'), pointer = layer.firstElementChild;
const motion = wt.querySelector('#motion');
let enabled = true;
try { enabled = localStorage.getItem('dumptotxt-motion') !== 'off'; } catch {}
let cur = [0, 0], run = 0, timers = [];

const at = (x, y, k = 1) => `translate(${x}px, ${y}px) scale(${k})`;
const ease = t => 0.5 - 0.5 * Math.cos(Math.PI * t);
const later = (ms, fn) => timers.push(setTimeout(fn, ms));
const wait = ms => new Promise(r => later(ms, r));
// A point inside an element, as fractions of its box, in the cursor layer's coordinates.
function pt(el, fx, fy) {
  const r = el.getBoundingClientRect(), b = layer.getBoundingClientRect();
  return [r.left - b.left + r.width * fx, r.top - b.top + r.height * fy];
}
const rest = k => pt(scenes[k], 0.618, 0.618); // lower-right golden point
function reset(k) {
  const s = scenes[k];
  [s.className, s.innerHTML] = pristine[k];
  return s;
}
const open = s => s.querySelector('.win').animate([{opacity: 0, transform: 'scale(.95)'}, {opacity: 1, transform: 'none'}], {duration: 300, easing: 'cubic-bezier(.2,.8,.2,1)'});

// A cursor timeline of T ms starting where the cursor is now.
function track(T) {
  const ks = [{offset: 0, transform: at(...cur)}], o = ms => Math.min(ms / T, 1);
  const t = {
    hold(ms, k = 1) { ks.push({offset: o(ms), transform: at(cur[0], cur[1], k)}); return t; },
    // Curve to `to`, bowed sideways by `bend` of the distance: hands don't move in straight lines.
    go(t0, t1, to, bend = 0.15, each) {
      const p = cur, cx = (p[0] + to[0]) / 2 - (to[1] - p[1]) * bend, cy = (p[1] + to[1]) / 2 + (to[0] - p[0]) * bend;
      t.hold(t0);
      const n = Math.max(12, Math.ceil((t1 - t0) / 25));
      for (let i = 1; i <= n; i++) {
        const u = ease(i / n), v = 1 - u, ms = t0 + (t1 - t0) * i / n;
        const x = v * v * p[0] + 2 * v * u * cx + u * u * to[0], y = v * v * p[1] + 2 * v * u * cy + u * u * to[1];
        ks.push({offset: o(ms), transform: at(x, y)});
        each?.(ms, x, y);
      }
      cur = to;
      return t;
    },
    click(ms) { return t.hold(ms).hold(ms + 60, 0.85).hold(ms + 140); },
    play() {
      ks.push({offset: 1, transform: at(...cur)});
      pointer.getAnimations().forEach(a => a.cancel());
      pointer.style.transform = at(...cur);
      pointer.animate(ks, T);
    },
  };
  return t;
}

function explorer() {
  const s = reset(0), menu = s.querySelector('.cmenu');
  const entries = [...menu.querySelectorAll('b, span')], dump = menu.querySelector('.dump');
  s.classList.remove('picked', 'menu-open');
  dump.classList.remove('hot');
  const T = 2900, tr = track(T), boxes = entries.map(e => [e, pt(e, 0, 0), pt(e, 1, 1)]);
  // No room below the row, so the menu opens upward with its lower-left corner at the click.
  tr.go(0, 850, pt(menu, 0, 1), 0.15).click(1000);
  later(600, () => s.classList.add('hover'));
  later(1060, () => s.classList.add('picked', 'menu-open'));
  // Each entry lights up while the cursor is over it.
  let last;
  tr.go(1350, 2000, pt(dump, 0.45, 0.5), -0.08, (ms, x, y) => {
    const hit = boxes.find(([, a, b]) => x >= a[0] && x <= b[0] && y >= a[1] && y <= b[1])?.[0];
    if (hit !== last) { last = hit; later(ms, () => entries.forEach(e => e.classList.toggle('hot', e === hit))); }
  }).click(2250);
  later(2350, () => s.classList.remove('menu-open', 'hover'));
  tr.play();
  return T;
}

function review() {
  const s = reset(1), rows = [...s.querySelectorAll('.trow')], sum = s.querySelector('.sum'), create = s.querySelector('.create');
  const T = 3900, tr = track(T), box = pt(rows[0].querySelector('.cb'), 0.5, 0.5), button = pt(create, 0.5, 0.55);
  open(s);
  // Discovery fills the tree row by row.
  s.classList.add('scanning');
  s.querySelector('.scan').textContent = 'Scanning…';
  sum.textContent = '0 B selected · 0 paths';
  rows.forEach((r, i) => later(250 + i * 160, () => { r.classList.add('in'); sum.textContent = `${[44, 105, 105, 173][i]} B selected · ${i + 1} paths`; }));
  later(950, () => { s.classList.remove('scanning'); s.querySelector('.scan').textContent = 'Scan complete'; });
  tr.go(0, 1150, box, 0.12).click(1350);
  later(1410, () => {
    s.classList.add('excluded');
    sum.textContent = '129 B selected · 4 paths';
    s.querySelectorAll('.share').forEach(e => { e.style.setProperty('--s', e.dataset.b); e.lastChild.textContent = `${e.dataset.b}%`; });
  });
  tr.go(1750, 2350, button, -0.1).click(2550);
  later(2560, () => create.classList.add('press'));
  later(2700, () => { create.classList.remove('press'); s.classList.add('exporting'); });
  tr.play();
  return T;
}

function notepad() {
  const s = reset(2), text = s.querySelector('.text'), pre = text.firstElementChild;
  const T = 4700, tr = track(T), p = pt(text, 0.6, 0.45), out = pt(s, 0.75, 1);
  open(s);
  tr.go(0, 900, p, 0.15).hold(3900).go(3900, 4500, [out[0], out[1] + 40], 0.1).play();
  // Three notches of the wheel, then down to the files.
  [6, 13, 20, 27].forEach((n, i) => later(1250 + i * 650, () => { pre.style.transform = `translateY(${-n * 1.4}em)`; }));
  pointer.animate([{opacity: 1}, {offset: 3950 / T, opacity: 1}, {offset: 4400 / T, opacity: 0}, {opacity: 0}], {duration: T, fill: 'forwards'});
  return T;
}

const stages = [explorer, review, notepad];
function show(k) {
  items.forEach((li, i) => li.classList.toggle('now', i === k));
  if (k >= 0) items.forEach((li, i) => li.classList.toggle('view', i === k));
}
async function play(id, from) {
  const on = () => id === run;
  for (let k = from; ; k = (k + 1) % 3) {
    show(k);
    await wait(stages[k]());
    if (!on()) return;
    if (k === 2) {
      show(-1);
      await wait(700);
      if (!on()) return;
      // Fades back in on card 1.
      cur = rest(0);
      track(500).play();
      pointer.animate([{opacity: 0}, {opacity: 1}], 500);
      await wait(600);
      if (!on()) return;
    }
  }
}
function stop() {
  run++;
  timers.forEach(clearTimeout);
  timers = [];
  pointer.getAnimations().forEach(a => a.cancel());
}
function start(from = 0) {
  stop();
  wt.classList.toggle('playing', enabled);
  motion.setAttribute('aria-pressed', String(enabled));
  motion.textContent = enabled ? 'Animations on' : 'Animations off';
  if (!enabled) {
    scenes.forEach((s, k) => reset(k));
    show(-1);
    return;
  }
  // Starts on the chosen card once layout has settled.
  later(100, () => {
    cur = rest(from);
    pointer.style.transform = at(...cur);
    play(run, from);
  });
}

wt.querySelector('.demo-controls').hidden = false;
wt.querySelector('#replay').addEventListener('click', () => start(0));
motion.addEventListener('click', () => {
  enabled = !enabled;
  try { localStorage.setItem('dumptotxt-motion', enabled ? 'on' : 'off'); } catch {}
  start(0);
});
// A click on a step title or its card replays that step.
wt.querySelectorAll('.step, .frame').forEach(el => {
  if (el.classList.contains('frame')) { el.tabIndex = 0; el.setAttribute('role', 'button'); }
  const go = () => { if (enabled) start(Number(el.dataset.step)); };
  el.addEventListener('click', go);
  el.addEventListener('keydown', e => { if (el.classList.contains('frame') && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); go(); } });
});
// Resizing moves every measured target, and a hidden tab stalls the cursor's clock: start over afterwards.
let settle;
addEventListener('resize', () => { clearTimeout(settle); settle = setTimeout(() => start(0), 250); });
document.addEventListener('visibilitychange', () => { if (!document.hidden) start(0); });
start(0);
