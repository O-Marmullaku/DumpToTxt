import {formatDemo} from './demo.mjs';

const choices = [...document.querySelectorAll('input[name="file"]')];
const format = document.querySelector('#format');
document.querySelector('.file-tree').disabled = false;
format.disabled = false;
function updateExample() {
  const selected = choices.filter(input => input.checked).map(input => input.value);
  document.querySelector('#output-preview').textContent = formatDemo(selected, format.value);
  document.querySelector('#file-count').textContent = `${selected.length} ${selected.length === 1 ? 'file' : 'files'} included`;
}
for (const input of choices) input.addEventListener('change', updateExample);
format.addEventListener('change', updateExample);
updateExample();

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
