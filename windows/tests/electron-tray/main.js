// Stands in for Claude Desktop in CI, with its tray behavior (Claude 3.6556 on Windows): a main window that closes to the
// tray (hidden, like Claude's X); a tray icon whose left click toggles the always-on-top quick entry and whose right click
// pops up a menu starting with "Show App", which shows the main window. The quick entry also pops up by itself later
// (what a taskbar click on Claude shows), and closes the tray menu when it loses the focus.
// Writes whether the main window ended up shown, and whether its page knows it is visible (a window shown from outside
// stays "hidden" inside Chromium, so it looks frozen).
const { app, BrowserWindow, Menu, Tray, nativeImage } = require('electron');
const fs = require('fs');

const result = (process.argv.find(a => a.startsWith('--result=')) || '').slice('--result='.length);
const events = [];
const note = text => { events.push(`${(process.uptime()).toFixed(1)}s ${text}`); console.log(text); };
let win, quick, tray, leftClicks = 0, menus = 0, showApp = 0;

app.whenReady().then(() => {
  win = new BrowserWindow({ width: 640, height: 420, x: 80, y: 80, title: 'Claude' });
  win.on('page-title-updated', e => e.preventDefault());
  win.loadURL('data:text/html,<title>Claude</title><body><h1>Claude</h1><button>OK</button></body>');
  win.on('show', () => note('main shown'));
  win.on('hide', () => note('main hidden'));
  const icon = nativeImage.createFromDataURL('data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==');
  tray = new Tray(icon.resize({ width: 16, height: 16 }));
  tray.setToolTip('Claude');
  const menu = Menu.buildFromTemplate([
    { label: 'Show App', click: () => { showApp++; note('Show App'); win.show(); win.focus(); } },
    { type: 'separator' },
    { label: 'Exit', click: () => { note('Exit picked'); } },
  ]);
  tray.on('click', () => { leftClicks++; note('tray clicked'); quick.isVisible() ? quick.hide() : quick.show(); });
  tray.on('right-click', () => { menus++; note('tray menu'); tray.popUpContextMenu(menu); });
  quick = new BrowserWindow({ width: 360, height: 90, x: 400, y: 600, title: 'Quick entry', show: false, frame: false,
    resizable: false, alwaysOnTop: true, skipTaskbar: true });
  quick.loadURL('data:text/html,<title>Quick entry</title><input>');
  // Like Claude's: losing the focus closes the quick entry, and handing the focus back to the window before it closes an
  // open tray menu too.
  quick.on('blur', () => {
    if (!quick.isVisible()) return;
    note('quick entry blurred');
    quick.hide();
    setTimeout(() => { note('tray menu closed by the quick entry'); tray.closeContextMenu(); }, 50);
  });

  setTimeout(() => win.hide(), 4000);          // closed to the tray
  setTimeout(() => quick.show(), 9000);        // the taskbar click opens the quick entry
  setTimeout(async () => {
    const page = await win.webContents.executeJavaScript('document.visibilityState');
    const out = { mainVisible: win.isVisible(), page, leftClicks, menus, showApp, events };
    note(JSON.stringify(out));
    if (result) fs.writeFileSync(result, JSON.stringify(out, null, 1));
    app.quit();
  }, 14000);
});
