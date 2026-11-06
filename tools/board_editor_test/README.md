# Board editor "Number in order" browser test

Headless-browser check of the editor's "Number in order" mode (`MRR.Config/wwwroot/board-editor.html`).
It is not part of `dotnet test` because it needs a browser. The page is run against a stubbed API
(a 6x2 board with 3 flags, 2 starts and a reboot token), so no server or database is needed.

```
python tools/board_editor_test/make_numbering_test.py MRR.Config/wwwroot/board-editor.html "$TEMP/editor_test.html"
msedge --headless --disable-gpu --virtual-time-budget=5000 --dump-dom "file:///<TEMP>/editor_test.html?board=7"
```

`?board=7` is required: the editor loads the board named in the URL. Each check prints as a `PASS`/`FAIL`
line inside `<pre id="result">`; `FAIL exception: ' + e.message ...` and `PASS ' : 'FAIL ' ...` lines are the
test script's own source text echoed into the page and can be ignored. Last run 2026-11-06: 26 checks passed.
