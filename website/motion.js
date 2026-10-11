const workflow = document.querySelector('.workflow');
const steps = [...workflow.children];
const links = steps.map(step => step.querySelector('.app-shot > a'));
const controls = document.querySelector('.motion-controls');
const toggle = document.querySelector('#motion-toggle');
const replay = document.querySelector('#motion-replay');
let enabled = true;
try { enabled = localStorage.getItem('dumptotxt-motion') !== 'off'; } catch {}
let animations = [];
let inView = false;
let ready = false;
const duration = 10500;

const cursor = document.createElement('span');
cursor.className = 'demo-cursor';
cursor.setAttribute('aria-hidden', 'true');
cursor.innerHTML = '<svg viewBox="0 0 28 34"><path d="M3 2v26l7-7 5 11 5-2-5-11h10Z"/></svg>';
workflow.append(cursor);
const before = document.createElement('img');
before.src = 'assets/review-before.png';
before.alt = '';
before.className = 'selection-before';
before.setAttribute('aria-hidden', 'true');
links[1].append(before);

function reset() {
  animations.forEach(animation => animation.cancel());
  animations = [];
  workflow.classList.remove('playing');
}
function sync() {
  animations.forEach(animation => {
    if (animation.playState === 'finished') return;
    if (enabled && inView && !document.hidden) animation.play();
    else animation.pause();
  });
}
function animate(element, frames) {
  const animation = element.animate(frames, { duration, fill: 'both', easing: 'linear' });
  animations.push(animation);
  return animation;
}
function run() {
  reset();
  if (!enabled || !ready) return;
  workflow.classList.add('playing');
  const bounds = workflow.getBoundingClientRect();
  const point = (index, x, y) => {
    const image = links[index].querySelector('img').getBoundingClientRect();
    return `translate(${image.left - bounds.left + image.width * x}px, ${image.top - bounds.top + image.height * y}px)`;
  };
  const menu = point(0, .42, .163);
  const checkbox = point(1, .341, .161);
  const create = point(1, .91, .96);
  const output = point(2, .45, .28);
  animate(cursor, [
    {offset:0,transform:point(0,.9,.3),opacity:0},
    {offset:.04,transform:point(0,.9,.3),opacity:1},
    {offset:.13,transform:menu},{offset:.2,transform:menu},
    {offset:.35,transform:checkbox},{offset:.45,transform:checkbox},
    {offset:.57,transform:create},{offset:.64,transform:create},
    {offset:.78,transform:output,opacity:1},
    {offset:.87,transform:output,opacity:0},{offset:1,transform:output,opacity:0}
  ].map(frame => ({...frame, easing: 'cubic-bezier(.16,1,.3,1)'})));
  // The two selection frames are unedited captures of the production window.
  animate(before, [{offset:0,opacity:1},{offset:.405,opacity:1},{offset:.41,opacity:0},{offset:1,opacity:0}]);
  animate(links[2], [{offset:0,filter:'blur(2px)',opacity:.35},{offset:.65,filter:'blur(2px)',opacity:.35},{offset:.8,filter:'blur(0px)',opacity:1},{offset:1,filter:'blur(0px)',opacity:1}]);
  for (const [index, start, end] of [[0,.04,.25],[1,.3,.65],[2,.68,.95]]) {
    animate(steps[index].querySelector('h2'), [{offset:0,color:'#202020'},{offset:start-.01,color:'#202020'},{offset:start,color:'#1967d2'},{offset:end,color:'#1967d2'},{offset:Math.min(1,end+.04),color:'#202020'},{offset:1,color:'#202020'}]);
  }
  for (const [position, offset] of [[menu,.17],[checkbox,.41],[create,.61]]) {
    const ring = document.createElement('span');
    ring.className = 'demo-click';
    ring.setAttribute('aria-hidden', 'true');
    workflow.append(ring);
    const animation = animate(ring, [{offset:0,transform:position+' scale(.2)',opacity:0},{offset:offset-.001,transform:position+' scale(.2)',opacity:0},{offset,transform:position+' scale(.2)',opacity:.8},{offset:offset+.065,transform:position+' scale(1.6)',opacity:0},{offset:1,transform:position+' scale(1.6)',opacity:0}]);
    animation.finished.then(() => ring.remove(), () => ring.remove());
  }
  animations[0].finished.then(() => workflow.classList.remove('playing'), () => {});
  sync();
}
function updateControl() {
  toggle.textContent = enabled ? 'Animations on' : 'Animations off';
  toggle.setAttribute('aria-pressed', String(enabled));
  replay.disabled = !enabled;
}
toggle.addEventListener('click', () => {
  enabled = !enabled;
  try { localStorage.setItem('dumptotxt-motion', enabled ? 'on' : 'off'); } catch {}
  updateControl();
  if (enabled) run(); else reset();
});
replay.addEventListener('click', run);
document.addEventListener('visibilitychange', sync);
new IntersectionObserver(([entry]) => { inView = entry.isIntersecting; sync(); }, {threshold:.08}).observe(workflow);
let resizeTimer;
window.addEventListener('resize', () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(run, 150); });
Promise.all([...links.map(link => link.querySelector('img')), before].map(img => img.decode())).then(() => {
  ready = true;
  controls.hidden = false;
  updateControl();
  run();
}).catch(() => reset());
