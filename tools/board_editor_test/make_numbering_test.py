import sys, json

src, out = sys.argv[1], sys.argv[2]
# Tile images are fetched relative to the page; point them at the real wwwroot so the page's
# img.onerror fallback does not loop when it is opened from a temp folder.
import os, pathlib
BASE = pathlib.Path(os.path.dirname(os.path.abspath(src))).as_uri() + '/'
html = open(src, encoding='utf-8').read().replace('\r\n', '\n')

STUB = r'''
<script>
// Fake API: one 6x2 board with 3 flags (wrongly numbered 3,1,1), 2 starts and a reboot token.
const FAKE_ITEMS = [
  {x:0,y:0,squareType:100,rotation:0},{x:2,y:0,squareType:100,rotation:0},{x:4,y:0,squareType:100,rotation:0},
  {x:1,y:1,squareType:110,rotation:1},{x:3,y:1,squareType:110,rotation:1},{x:5,y:1,squareType:120,rotation:1}];
const FAKE_ACTIONS = [
  {x:0,y:0,squareAction:16,sequence:1,phase:31,parameter:3},{x:2,y:0,squareAction:16,sequence:1,phase:31,parameter:1},
  {x:4,y:0,squareAction:16,sequence:1,phase:31,parameter:1},
  {x:1,y:1,squareAction:19,sequence:1,phase:31,parameter:2},{x:3,y:1,squareAction:19,sequence:1,phase:31,parameter:2},
  {x:5,y:1,squareAction:25,sequence:1,phase:31,parameter:3}];
const json = o => Promise.resolve({ ok: true, json: () => Promise.resolve(o) });
window.fetch = (url) => {
  url = String(url);
  if (url.endsWith('/types')) return json([]);
  if (url.endsWith('/boards')) return json({ boards: [{ boardId: 7, boardName: 'T' }] });
  if (url.endsWith('/gamedata')) return json({ gameData: [] });
  if (/\/api\/boardeditor\/7$/.test(url)) return json({ boardId: 7, boardName: 'T', x: 6, y: 2, laserDamage: 1, phaseCount: 5, gameType: 0, items: FAKE_ITEMS, actions: FAKE_ACTIONS });
  return Promise.resolve({ ok: false, json: () => Promise.resolve({}) });
};
sessionStorage.setItem('boardImport.review.7', JSON.stringify([
  { x: 0, y: 0, reason: 'Matched element100-3_top.png: the number or letter is not read reliably from a photo; check it.' },
  { x: 1, y: 1, reason: 'Close call' }]));
</script>
'''

TEST = r'''
<pre id="result"></pre>
<script>
(async () => {
  const log = [];
  const check = (name, ok, detail) => log.push((ok ? 'PASS ' : 'FAIL ') + name + (ok ? '' : '  -> ' + detail));
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const click = (x, y) => document.getElementById(`cell-${x}-${y}`)
      .dispatchEvent(new MouseEvent('mousedown', { button: 0, bubbles: true }));
  const up = (x, y) => document.getElementById(`cell-${x}-${y}`)
      .dispatchEvent(new MouseEvent('mouseup', { button: 0, bubbles: true }));
  const msg = () => document.getElementById('numberingMsg').textContent;
  const labels = () => [...document.querySelectorAll('.cell')].map(c => c.querySelector('.cell-label, .cell-icon')?.textContent || '').join(',');
  const key = (k) => document.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }));
  const param = (x, y, sa) => grid[y][x].actions.find(a => a.squareAction === sa).parameter;
  try {
    await wait(500);
    check('board loaded', grid.length === 2 && grid[0].length === 6, JSON.stringify([grid.length]));
    check('hint shown after an import with numbered tiles', msg().includes('Number in order'), msg());

    // --- flags ---
    document.getElementById('numberKind').value = 'flag';
    toggleNumbering();
    check('mode on', numbering !== null);
    check('3 flags pending, outlined', document.querySelectorAll('.cell.numbering-pending').length === 3,
      document.querySelectorAll('.cell.numbering-pending').length);
    check('pending flags show F?', labels().split(',').filter(l => l === 'F?').length === 3, labels());
    check('starts untouched while numbering flags', param(1, 1, 19) === 2);
    check('prompt asks for flag 1', msg().startsWith('Click flag 1 (3 left)'), msg());

    click(2, 0); up(2, 0);
    check('click sets flag 1', param(2, 0, 16) === 1);
    check('review mark cleared nowhere else yet', reviewCells.has('0,0'));
    click(1, 1);
    check('clicking a start while numbering flags is refused', msg().includes('is not a flag') && param(1, 1, 19) === 2, msg());
    click(2, 0);
    check('clicking a numbered flag again is refused', msg().includes('already flag 1'), msg());
    click(4, 0);
    check('second click sets flag 2', param(4, 0, 16) === 2);
    key('Backspace');
    check('Backspace undoes flag 2 (old number back, pending again)', param(4, 0, 16) === 1 && numbering.pending.has('4,0') && numbering.next === 2,
      JSON.stringify([param(4, 0, 16), numbering.next]));
    click(0, 0);
    check('flag clicked out of order gets the next number', param(0, 0, 16) === 2);
    key('Backspace');
    click(4, 0);
    click(0, 0);
    check('flags end up 3,1,2 by click order 2,0 -> 4,0 -> 0,0', param(2, 0, 16) === 1 && param(4, 0, 16) === 2 && param(0, 0, 16) === 3,
      JSON.stringify([param(2, 0, 16), param(4, 0, 16), param(0, 0, 16)]));
    check('finishes by itself when every flag has a number', numbering === null && msg() === 'All flags numbered.', msg());
    check('no outlines left', document.querySelectorAll('.cell.numbering-pending').length === 0);
    check('review mark for a numbered tile cleared, others kept', !reviewCells.has('0,0') && reviewCells.has('1,1'));
    check('board marked as having unsaved changes', isDirty === true);
    check('numbered flag shows its tile art', document.getElementById('cell-4-0').querySelector('img').src.endsWith('element100-2_top.png'),
      document.getElementById('cell-4-0').querySelector('img').src);

    // --- reboot tokens (letters) ---
    document.getElementById('numberKind').value = 'respawn';
    toggleNumbering();
    check('prompt asks for reboot token A', msg().startsWith('Click reboot token A'), msg());
    check('pending reboot token shows R?', document.getElementById('cell-5-1').querySelector('.cell-icon').textContent === 'R?');
    click(5, 1);
    check('reboot token becomes A (parameter 1)', param(5, 1, 25) === 1 && document.getElementById('cell-5-1').querySelector('.cell-icon').textContent === 'RA');

    // --- starts, stopped early ---
    document.getElementById('numberKind').value = 'start';
    toggleNumbering();
    click(3, 1);
    key('Escape');
    check('Esc stops; the one clicked is start 1, the other keeps its old number',
      numbering === null && param(3, 1, 19) === 1 && param(1, 1, 19) === 2 && msg().startsWith('Stopped: 1 numbered, 1 left'), msg());
    check('select is enabled again', document.getElementById('numberKind').disabled === false);

    // --- ordinary editing still works with the mode off ---
    click(1, 1); up(1, 1);
    check('with the mode off a click selects the cell', selectedCell && selectedCell.x === 1 && selectedCell.y === 1, JSON.stringify(selectedCell));

    // --- no tiles of a kind ---
    grid[1][3].actions = []; grid[1][1].actions = [];
    document.getElementById('numberKind').value = 'start';
    toggleNumbering();
    check('no tiles of that kind: mode stays off', numbering === null);
  } catch (e) {
    log.push('FAIL exception: ' + e.message + '\n' + e.stack);
  }
  document.getElementById('result').textContent = log.join('\n');
})();
</script>
'''

assert html.count('<head>') == 1 and html.rstrip().endswith('</html>')
html = html.replace('<head>', '<head><base href="' + BASE + '">' + STUB, 1)
html = html.rstrip()[:-len('</html>')].rstrip()
assert html.endswith('</body>')
html = html[:-len('</body>')] + TEST + '</body></html>'
open(out, 'w', encoding='utf-8').write(html)
print('wrote', out)
