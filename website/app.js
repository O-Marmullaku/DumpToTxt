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
  [0, 'folder', 1300], [0, 'menu', 1600],
  [1, 'check', 1300], [1, 'check', 1100], [1, 'create', 1600],
  [2, 'copy', 1600], [2, 'copy', 2500]
];
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
  document.querySelector('.copy-result').textContent = enabled && beat === 6 ? 'Copied to clipboard' : 'Ready to use';
  cursor.classList.toggle('click', [1, 3, 4, 6].includes(beat));
  if (!enabled || !visible || document.hidden) return;
  const target = demo.querySelector(`[data-target="${beats[beat][1]}"]`).getBoundingClientRect();
  const box = demo.getBoundingClientRect();
  cursor.style.transform = `translate(${target.left - box.left + target.width * .6}px, ${target.top - box.top + target.height * .55}px)`;
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
document.querySelectorAll('.step').forEach(button => button.addEventListener('click', () => replay([0, 2, 5][Number(button.dataset.step)])));
document.addEventListener('visibilitychange', render);
window.addEventListener('resize', render);
new IntersectionObserver(([entry]) => { visible = entry.isIntersecting; render(); }, {threshold: .15}).observe(demo);
render();
