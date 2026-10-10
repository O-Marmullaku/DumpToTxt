export const files = [
  {path:'README.md', content:'# Weekend project\nA little room for a good idea.'},
  {path:'src/main.js', content:"const greeting = 'Hello, world!';\nconsole.log(greeting);"},
  {path:'src/styles.css', content:'body {\n  font-family: system-ui, sans-serif;\n}'}
];
export function formatDemo(selected, format) {
  const included = files.filter(file => selected.includes(file.path));
  const inventory = files.map(file => file.path);
  if (format === 'json') return JSON.stringify({folder:'weekend-project', inventory, files:included}, null, 2);
  const tree = 'weekend-project/\n' + inventory.map(path => '  ' + path).join('\n');
  if (format === 'md') return '# weekend-project\n\n```text\n' + tree + '\n```' + included.map(file => '\n\n## ' + file.path + '\n\n```' + (file.path.endsWith('.js') ? 'javascript' : file.path.endsWith('.css') ? 'css' : 'markdown') + '\n' + file.content + '\n```').join('');
  return tree + included.map(file => '\n\n--- ' + file.path + ' ---\n' + file.content).join('');
}
