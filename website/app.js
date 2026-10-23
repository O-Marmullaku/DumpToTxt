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
// 2) the review window lists the files, the cursor unticks cities.json, flips the switch and clicks Create dump;
// 3) .txt file: Notepad opens the dump by itself and the cursor scrolls to the code. Clipboard: the cursor clicks
//    the browser behind Notepad to bring it forward, pastes the dump into ChatGPT, types a question and sends it.
// Passes alternate, so the switch flips every time. Clicking Create dump starts the next step at once, and both
// cards stay lit for a moment.
// Targets are measured just before each scene starts; the scene's own changes are timed off the same clock.
const wt = document.querySelector('#walkthrough');
const items = [...wt.querySelectorAll('.steps > li')];
const scenes = items.map(li => li.querySelector('.scene'));
const pristine = scenes.map(s => [s.className, s.innerHTML]);
const layer = wt.querySelector('.cursor-layer'), pointer = layer.firstElementChild;
let cur = [0, 0], run = 0, timers = [], clip = false;

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
const open = w => w.animate([{opacity: 0, transform: 'scale(.95)'}, {opacity: 1, transform: 'none'}], {duration: 300, easing: 'cubic-bezier(.2,.8,.2,1)'});

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
  const entries = [...menu.querySelectorAll('span')], dump = menu.querySelector('.dump');
  s.classList.remove('picked', 'menu-open');
  dump.classList.remove('hot');
  const T = 2250, tr = track(T), boxes = entries.map(e => [e, pt(e, 0, 0), pt(e, 1, 1)]);
  // The menu opens with its top-left corner at the click.
  tr.go(0, 850, pt(menu, 0, 0), 0.15).click(1000);
  later(600, () => s.classList.add('hover'));
  later(1060, () => s.classList.add('picked', 'menu-open'));
  // Each entry lights up while the cursor is over it.
  let last;
  tr.go(1300, 1800, pt(dump, 0.4, 0.5), 0.12, (ms, x, y) => {
    const hit = boxes.find(([, a, b]) => x >= a[0] && x <= b[0] && y >= a[1] && y <= b[1])?.[0];
    if (hit !== last) { last = hit; later(ms, () => entries.forEach(e => e.classList.toggle('hot', e === hit))); }
  }).click(2100);
  later(2200, () => s.classList.remove('menu-open', 'hover'));
  tr.play();
  return T;
}

function review() {
  // The window opens on the last pass's choice. Set before anything is measured: a style flush in between would
  // let the switch slide over as the window opens.
  const s = reset(1), rows = [...s.querySelectorAll('.trow')], create = s.querySelector('.create');
  s.classList.toggle('clip', !clip);
  s.classList.add('scanning'); // the files come in as the folder is scanned; hidden before the first paint
  const T = 3250, tr = track(T), box = pt(rows[0].querySelector('.cb'), 0.5, 0.5), button = pt(create, 0.5, 0.55);
  const toggle = pt(s.querySelector('.track'), 0.5, 0.5);
  open(s.querySelector('.win'));
  rows.forEach((r, i) => later(200 + i * 150, () => r.classList.add('in')));
  tr.go(0, 1000, box, 0.12).click(1200);
  later(1260, () => { s.classList.add('excluded'); s.querySelector('.sum').textContent = '2 files · 129 B'; });
  tr.go(1500, 2000, toggle, 0.1).click(2150);
  later(2210, () => s.classList.toggle('clip', clip));
  tr.go(2450, 2950, button, -0.1).click(3100);
  later(3110, () => create.classList.add('press'));
  later(3260, () => { create.classList.remove('press'); if (clip) s.classList.add('copied'); });
  tr.play();
  return T;
}

// Step 3 shows Notepad or the browser, whichever the pass sent the dump to; the other one is reset out of sight.
function blankChat(c) {
  c.className = 'win chat';
  c.querySelector('.typed').textContent = '';
}

// DumpToTxt opens the saved file itself: Notepad pops up in front.
function notepad() {
  const s = scenes[2], np = s.querySelector('.notepad'), text = np.querySelector('.text'), pre = text.firstElementChild;
  const T = 3200, tr = track(T), p = pt(text, 0.6, 0.5);
  pre.style.transition = 'none';
  pre.style.transform = '';
  pre.offsetWidth; // apply the scroll reset before the transition comes back
  pre.style.transition = '';
  s.classList.remove('clip', 'fresh');
  np.classList.remove('closed');
  open(np);
  later(400, () => blankChat(s.querySelector('.chat'))); // a fresh chat behind, for the next clipboard pass
  tr.go(0, 900, p, 0.15).play();
  // One turn of the wheel down to the code.
  later(1500, () => { pre.style.transform = 'translateY(-4.35em)'; });
  return T;
}

// Each clipboard pass asks ChatGPT one question at random: usually a real one, now and then (5%) a silly one.
// Never the same one twice running. Kept short enough to fit the chat bubble.
const questions = {
  real: [
    'judge the code quality',
    'how does the weather api work?',
    'check for security vulnerabilities!',
    'explain this project to me',
    'find the bug in forecast.js',
    'write tests for this',
    'add a 5-day forecast',
    'why is it always 22 C and sunny?',
    'convert this to typescript',
    'what would you refactor first?',
  ],
  silly: [
    'why am i such a bad programmer?',
    'where the fuck is cities[] defined?', // cities.json was unticked in step 2
    'is this code sentient?',
    'rewrite it in rust. all of it.',
    "explain it like i'm a dog",
    'will this get me hired at google?',
    'rate my code 1-10, be gentle',
    'can it run on my fridge?',
    'who wrote this garbage? (me)',
  ],
};
let lastQuestion;
function nextQuestion() {
  const pool = Math.random() < 0.05 ? questions.silly : questions.real;
  let q;
  do q = pool[Math.floor(Math.random() * pool.length)]; while (q === lastQuestion);
  return (lastQuestion = q);
}

// Copying opens nothing in Notepad: the browser comes up instead, and the cursor pastes into ChatGPT and asks.
function chat() {
  const s = scenes[2], c = s.querySelector('.chat'), typed = c.querySelector('.typed');
  const text = nextQuestion();
  blankChat(c);
  c.querySelector('.msg').textContent = text;
  s.classList.remove('fresh');
  s.classList.add('clip');
  open(c);
  // Typing takes as long as the question; the send click follows it.
  const e = 2850 + text.length * 55, T = e + 1700, tr = track(T);
  // The hand stays put for the Ctrl+V, then moves off the window, just below it, so the typing stays readable.
  tr.go(0, 800, pt(c.querySelector('.line'), 0.3, 0.5), 0.15).click(950)
    .go(1750, 2250, pt(c, 0.6, 1.04), 0.1)
    .go(e, e + 500, pt(c.querySelector('.send'), 0.5, 0.5), -0.1).click(e + 650).play();
  later(1000, () => c.classList.add('focus'));
  later(1150, () => c.classList.add('keys-on'));
  later(1400, () => c.classList.add('pasting', 'ready'));
  later(1800, () => c.classList.remove('keys-on'));
  [...text].forEach((_, i) => later(2650 + i * 55, () => { typed.textContent = text.slice(0, i + 1); }));
  later(e + 710, () => { typed.textContent = ''; c.classList.remove('pasting', 'ready', 'focus'); c.classList.add('sent-on'); });
  return T;
}

// Once the dump has been seen, its windows quietly close on their own; the cursor just heads back to the folder.
const close = w => w.animate([{opacity: 1, transform: 'none'}, {opacity: 0, transform: 'scale(.95)'}], {duration: 180, easing: 'ease-in'})
  .finished.then(() => w.classList.add('closed'), () => {});

const stages = [explorer, review, () => (clip ? chat : notepad)()];
function show(k) {
  items.forEach((li, i) => li.classList.toggle('now', i === k));
  if (k >= 0) items.forEach((li, i) => { li.classList.toggle('view', i === k); li.classList.toggle('under', i < k); });
}
async function play(id, from) {
  const on = () => id === run;
  for (let k = from; ; k = (k + 1) % 3) {
    // The step that just handed over stays lit a moment beside the new one.
    const prev = items.findIndex(li => li.classList.contains('now'));
    show(k);
    if (prev >= 0 && prev !== k) { items[prev].classList.add('now'); later(700, () => items[prev].classList.remove('now')); }
    await wait(stages[k]());
    if (!on()) return;
    if (k === 2) {
      await wait(900);
      if (!on()) return;
      close(scenes[2].querySelector(clip ? '.chat' : '.notepad'));
      close(scenes[1].querySelector('.win'));
      await wait(300);
      if (!on()) return;
      clip = !clip; // the next pass takes the other route
      scenes[1].classList.remove('copied');
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
  wt.classList.add('playing');
  show(-1); // a jump isn't a handover: nothing stays lit from before
  if (from === 0) clip = false;
  wt.querySelectorAll('.closed').forEach(w => w.classList.remove('closed')); // a jump reopens what the last pass closed
  scenes[2].classList.toggle('clip', clip);
  scenes[2].classList.toggle('fresh', from < 2); // no dump exists yet: Notepad appears only once step 2 makes one
  // Starts on the chosen card once layout has settled.
  later(100, () => {
    cur = rest(from);
    pointer.style.transform = at(...cur);
    play(run, from);
  });
}

// A click on a step title or its card replays that step.
wt.querySelectorAll('.step, .frame').forEach(el => {
  const go = () => start(Number(el.dataset.step));
  el.addEventListener('click', go);
});
// A width change moves every measured target, and a hidden tab stalls the cursor's clock: start over afterwards.
// Height-only resizes (a phone's address bar showing or hiding on scroll) move nothing, so they don't restart it.
let settle, width = innerWidth;
addEventListener('resize', () => {
  if (innerWidth === width) return;
  width = innerWidth;
  clearTimeout(settle);
  settle = setTimeout(() => start(0), 250);
});
document.addEventListener('visibilitychange', () => { if (!document.hidden) start(0); });
start(0);


// The terminal takes commands. Spelling Repomix right for once wipes the smile off the laughing emojis.
const term = document.querySelector('.term'), tpre = term.querySelector('pre'), tin = term.querySelector('.tin');
const live = tpre.querySelector('.live'), typed = live.querySelector('.cmd');
const esc = t => t.replace(/[&<>]/g, c => ({'&': '&amp;', '<': '&lt;', '>': '&gt;'})[c]);
const frag = html => { const t = document.createElement('template'); t.innerHTML = html; return t.content; };
tin.hidden = false;
function enter(raw) {
  const cmd = raw.trim(), lines = [`C:\\Projects\\weather-app&gt;${cmd ? ' ' + esc(cmd) : ''}`];
  if (/^npx repomix(@latest)?$/i.test(cmd)) {
    lines.push('<span class="ok">✓ success</span>', '...wait, you spelled it right?', '');
    tpre.querySelectorAll('.lol').forEach(e => { e.float?.remove(); e.replaceWith(frag('<span class="lol">😐</span>')); });
    laughs();
  } else if (cmd) lines.push(`'${esc(cmd.split(/\s+/)[0])}' is not recognized as an internal or external command,`, 'operable program or batch file.', '');
  live.before(frag(lines.map(l => l + '\n').join('')));
  tpre.scrollTop = tpre.scrollHeight;
}
// The laughs float over the whole terminal, edges and all: the pre only keeps an invisible stand-in where each one
// sits, and the floating copy follows it as the pre scrolls. They sit just right of the typo, on its line, pulled back
// in where the line runs to the edge so they stay on screen.
const fig = term.parentElement;
function laughs() {
  const f = fig.getBoundingClientRect(), p = tpre.getBoundingClientRect();
  tpre.querySelectorAll('.lol').forEach(a => {
    const e = a.float ??= fig.appendChild(a.cloneNode(true)), y = a.offsetTop - tpre.scrollTop;
    e.className = a.className + ' float';
    e.hidden = y + a.offsetHeight < 0 || y > tpre.clientHeight;
    e.style.left = Math.min(p.left - f.left + a.offsetLeft, f.width - e.offsetWidth) + 'px';
    e.style.top = p.top - f.top + y + 'px';
  });
}
tpre.addEventListener('scroll', laughs);
addEventListener('resize', laughs);
tin.addEventListener('input', () => { typed.textContent = tin.value && ' ' + tin.value; tpre.scrollTop = tpre.scrollHeight; });
tin.addEventListener('keydown', e => {
  if (e.key !== 'Enter') return;
  e.preventDefault();
  enter(tin.value);
  tin.value = typed.textContent = '';
});
term.addEventListener('click', () => { if (!getSelection().toString()) tin.focus({preventScroll: true}); });

// The story replays once, the first time the terminal is fully in view: each command is typed at the prompt and
// its errors follow. Until then only the prompt shows; the window keeps its height so nothing below moves.
const story = tpre.innerHTML.slice(0, tpre.innerHTML.indexOf('<span class="live">')).split('\n').slice(0, -1);
term.style.minHeight = term.offsetHeight + 'px';
while (live.previousSibling) live.previousSibling.remove();
tin.disabled = true;
const pause = ms => new Promise(r => setTimeout(r, ms));
// People type in bursts: mostly quick keys, a beat before each new word, the odd hesitation. In a typed sentence
// (not the npx commands, whose typos go unnoticed) two letters may come out swapped and get backspaced.
async function type(text, slips) {
  typed.textContent = ' ';
  let slipped = !slips;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    let d = 35 + Math.random() ** 2 * 140;
    if (text[i - 1] === ' ') d += 80 + Math.random() * 180;
    if (Math.random() < 0.07) d += 250 + Math.random() * 350;
    await pause(d);
    if (!slipped && i > 3 && /\w\w/.test(text.slice(i, i + 2)) && Math.random() < 0.2) {
      slipped = true;
      typed.textContent += text[i + 1];
      await pause(60 + Math.random() * 60);
      typed.textContent += ch;
      await pause(250 + Math.random() * 250);
      for (let k = 0; k < 2; k++) { typed.textContent = typed.textContent.slice(0, -1); await pause(90 + Math.random() * 40); }
    }
    typed.textContent += ch;
  }
}
new IntersectionObserver(async ([e], seen) => {
  if (!e.isIntersecting) return;
  seen.disconnect();
  let failed = false, rounds = 0;
  for (const line of story) {
    // The second failed command cues the Explorer highlight after its errors finish.
    if (!line && failed) {
      failed = false;
      rounds++;
      if (rounds === 2) document.querySelector('.versus').classList.add('is-pointing');
      await pause(rounds > 1 ? 1300 : 500);
    }
    const command = line.match(/^C:\\Projects\\weather-app&gt; (.*)$/);
    if (command) {
      const f = frag(command[1]);
      const laugh = f.querySelector('.lol');
      laugh?.remove();
      await pause(600);
      await type(f.textContent, !/^npx /.test(f.textContent));
      if (laugh) {
        typed.replaceChildren(' ', f);
        await pause(300);
        typed.after(laugh);
        laughs();
      }
      await pause(laugh ? 700 : 350);
      laugh?.float?.remove();
      laugh?.remove();
      typed.textContent = '';
    } else await pause(90);
    const f = frag(line + '\n');
    if (f.querySelector('u')) failed = true;
    live.before(f);
    tpre.scrollTop = tpre.scrollHeight;
    laughs();
  }
  term.style.minHeight = '';
  tin.disabled = false;
}, {threshold: 0.9}).observe(term);

// The cat spins on each click. Spins add up, so clicking fast spins it faster; too fast and it rains cats.
const cat = document.querySelector('.wow');
const catArt = cat.querySelector('img');
cat.disabled = false;
let catClicks = [];
cat.addEventListener('click', () => {
  catArt.animate([{transform: 'rotate(0)'}, {transform: 'rotate(360deg)'}], {duration: 1000, easing: 'ease-in-out', composite: 'add'});
  const now = performance.now();
  catClicks = [...catClicks.filter(t => now - t < 3000), now];
  if (catClicks.length < 12) return;
  catClicks = [];
  for (let i = 0; i < 40; i++) {
    const c = document.createElement('img'), w = 30 + Math.random() * 50;
    c.src = catArt.src;
    c.alt = '';
    c.className = 'rain';
    c.style.width = w + 'px';
    c.style.left = Math.random() * (innerWidth - w) + 'px';
    document.body.append(c);
    c.animate([{transform: 'none'}, {transform: `translateY(${innerHeight + 240}px) rotate(${Math.random() * 720 - 360}deg)`}],
      {duration: 1800 + Math.random() * 1800, delay: Math.random() * 1500, easing: 'cubic-bezier(.4,0,.8,.6)'}).finished.then(() => c.remove());
  }
});
