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


const demo = document.querySelector('#walkthrough');
const motion = document.querySelector('#motion');
const cursor = document.querySelector('.demo-cursor');
const steps = [...document.querySelectorAll('.workflow > li')];
const beats = [
  [0, 'folder', 1600], [0, 'menu', 1900],
  [1, 'check', 1400], [1, 'check', 1000],
  [1, 'format', 1400], [1, 'format', 2400],
  [1, 'preview', 1400], [1, 'preview', 2100], [1, 'create', 1700],
  [2, 'copy', 1500], [2, 'copy', 2500]
];
// Curved, eased travel keeps one continuous pointer across the whole demonstration.
function moveCursor(x, y) {
  const matrix = new DOMMatrixReadOnly(getComputedStyle(cursor).transform);
  const start = [matrix.m41, matrix.m42];
  cursor.getAnimations().forEach(animation => animation.cancel());
  const dx = x - start[0], dy = y - start[1];
  const bend = Math.min(45, Math.hypot(dx, dy) * .12);
  const length = Math.hypot(dx, dy) || 1;
  const cx = (start[0] + x) / 2 - dy / length * bend;
  const cy = (start[1] + y) / 2 + dx / length * bend;
  const frames = Array.from({length: 49}, (_, i) => {
    const t = .5 - .5 * Math.cos(Math.PI * i / 48), v = 1 - t;
    return {transform: `translate(${v*v*start[0]+2*v*t*cx+t*t*x}px, ${v*v*start[1]+2*v*t*cy+t*t*y}px)`};
  });
  cursor.style.transform = `translate(${x}px, ${y}px)`;
  cursor.animate(frames, {duration: 1000, easing: 'linear'});
}
let enabled = true;
try { enabled = localStorage.getItem('dumptotxt-motion') !== 'off'; } catch {}
let beat = 0, timer, visible = true;
function render() {
  clearTimeout(timer);
  demo.classList.toggle('playing', enabled);
  motion.setAttribute('aria-pressed', String(enabled));
  motion.textContent = enabled ? 'Animations on' : 'Animations off';
  steps.forEach((step, index) => step.classList.toggle('active', enabled && index === beats[beat][0]));
  demo.dataset.beat = String(beat);
  document.querySelector('.file-count').textContent = enabled && beat < 3 ? '2' : '3';
  document.querySelector('.copy-result').textContent = enabled && beat === 10 ? 'Copied to clipboard' : 'Ready to use';
  cursor.classList.toggle('click', [1, 3, 5, 7, 8, 10].includes(beat));
  demo.classList.toggle('menu-open', !enabled || beat >= 1);
  demo.classList.toggle('files-selected', !enabled || beat >= 3);
  demo.classList.toggle('format-open', enabled && beat === 5);
  demo.classList.toggle('preview-open', enabled && beat === 7);
  demo.classList.toggle('output-ready', !enabled || beat >= 9);
  if (!enabled || !visible || document.hidden) {
    cursor.getAnimations().forEach(animation => animation.pause());
    return;
  }
  const target = demo.querySelector(`[data-target="${beats[beat][1]}"]`).getBoundingClientRect();
  const box = demo.getBoundingClientRect();
  moveCursor(target.left - box.left + target.width * .6, target.top - box.top + target.height * .55);
  timer = setTimeout(() => { beat = (beat + 1) % beats.length; render(); }, beats[beat][2]);
}
function replay(from = 0) { beat = from; render(); }
document.querySelector('.demo-controls').hidden = false;
document.querySelector('#replay').addEventListener('click', () => replay());
motion.addEventListener('click', () => {
  enabled = !enabled;
  try { localStorage.setItem('dumptotxt-motion', enabled ? 'on' : 'off'); } catch {}
  replay();
});
document.querySelectorAll('.step').forEach(button => button.addEventListener('click', () => replay([0, 2, 9][Number(button.dataset.step)])));
document.addEventListener('visibilitychange', render);
window.addEventListener('resize', render);
new IntersectionObserver(([entry]) => { visible = entry.isIntersecting; render(); }, {threshold: .15}).observe(demo);
render();
