(() => {
const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
const $ = (s, r = document) => r.querySelector(s), $$ = (s, r = document) => [...r.querySelectorAll(s)];
const clamp = (x, a = 0, b = 1) => Math.max(a, Math.min(b, x));
const seg = (p, a, b) => clamp((p - a) / (b - a));
const ease = (x) => 1 - Math.pow(1 - x, 3);
const lerp = (a, b, t) => a + (b - a) * t;

/* ---------- Icons (stroke, like the app's Fluent glyphs) ---------- */
const P = {
  gear: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
  x: '<path d="M6 6l12 12M18 6 6 18"/>', check: '<path d="M5 12.5l4.5 4.5L19 7"/>', plus: '<path d="M12 5v14M5 12h14"/>',
  down: '<path d="M6 9l6 6 6-6"/>', right: '<path d="M9 6l6 6-6 6"/>', list: '<path d="M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01"/>',
  undo: '<path d="M9 14 4 9l5-5"/><path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11"/>', redo: '<path d="m15 14 5-5-5-5"/><path d="M20 9H9.5a5.5 5.5 0 0 0 0 11H13"/>',
  upload: '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12"/>', min: '<path d="M5 12h14"/>', sq: '<rect x="5" y="5" width="14" height="14" rx="1"/>',
  scissors: '<circle cx="6" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M20 4 8.1 15.9M14.5 14.5 20 20M8.1 8.1 12 12"/>',
  pointer: '<path d="m4 4 7 17 2.5-7.5L21 11z"/>', ffwd: '<path d="m6 7 5 5-5 5M13 7l5 5-5 5"/>', ripple: '<path d="M6 4v16M10 12h10M16 8l4 4-4 4"/>',
  slip: '<path d="M12 4v16M8 8l-4 4 4 4M16 8l4 4-4 4"/>', hand: '<path d="M18 11V6a2 2 0 0 0-4 0v5M14 10V4a2 2 0 0 0-4 0v6M10 10.5V6a2 2 0 0 0-4 0v8"/><path d="M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.9-6-2.4l-3.6-3.6a2 2 0 0 1 2.8-2.8L7 15"/>',
  zoom: '<circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3M11 8v6M8 11h6"/>', trash: '<path d="M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6M10 11v6M14 11v6"/>',
  back: '<path d="M18 6H8M8 6v12M4 12h10"/>', mark: '<path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"/>',
  film: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m10 9 5 3-5 3z"/>', vol: '<path d="M11 5 6 9H2v6h4l5 4zM15.5 8.5a5 5 0 0 1 0 7M19 5a10 10 0 0 1 0 14"/>',
  type: '<path d="M4 7V4h16v3M9 20h6M12 4v16"/>', wand: '<path d="m15 4 5 5L9 20H4v-5zM13 6l5 5"/>', cc: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M10 10.5a2 2 0 1 0 0 3M17 10.5a2 2 0 1 0 0 3"/>',
  full: '<path d="M8 3H5a2 2 0 0 0-2 2v3M21 8V5a2 2 0 0 0-2-2h-3M3 16v3a2 2 0 0 0 2 2h3M16 21h3a2 2 0 0 0 2-2v-3"/>', eye: '<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
  lock: '<rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>', head: '<path d="M3 18v-6a9 9 0 0 1 18 0v6"/><path d="M21 19a2 2 0 0 1-2 2h-1v-6h3zM3 19a2 2 0 0 0 2 2h1v-6H3z"/>',
  skipb: '<path d="M19 20 9 12l10-8zM5 19V5"/>', stepb: '<path d="M17 18 9 12l8-6z"/>', stepf: '<path d="m7 6 8 6-8 6z"/>', skipf: '<path d="m5 4 10 8-10 8zM19 5v14"/>',
  play: '<path d="M7 4v16l13-8z"/>', move: '<path d="M5 9l-3 3 3 3M9 5l3-3 3 3M15 19l-3 3-3-3M19 9l3 3-3 3M2 12h20M12 2v20"/>', mute: '<path d="M11 5 6 9H2v6h4l5 4zM22 9l-6 6M16 9l6 6"/>',
  split: '<path d="M12 3v18M8 7l-4 5 4 5M16 7l4 5-4 5"/>', clip: '<rect x="3" y="7" width="18" height="10" rx="2"/>'
};
const I = (n) => `<svg viewBox="0 0 24 24" aria-hidden="true">${P[n]}</svg>`;

/* ---------- The panel, rebuilt from PanelForm / Dashboard.cs ---------- */
function panelHTML(id) {
  return `<div class="zc zp" id="${id}">
  <div class="zp-h"><span class="orb"></span><b>ZenithClipper</b><span class="st"><i></i>Recording</span><span class="sep"></span>${I('gear')}<span style="width:4px"></span>${I('x')}</div>
  <div class="zp-b">
    <div class="zp-toast" data-toast>${I('check')}<span>Saved 2m clip  ·  Clip-2026-11-17-21-42-08-3f9a1c.mp4</span><em>Edit</em></div>
    <div class="zp-row"><b>Instant replay</b><span class="zp-tog">Record<i></i></span></div>
    <div class="zp-big"><div><div class="zp-rec"><i></i>RECORDING</div><div class="zp-time" data-clock>21:11</div><div class="zp-sub">buffered of 1:00:00 history</div></div><div class="zp-ring" data-ring><div><b data-mb>611 MB</b><span>of 2.0 GB</span></div></div></div>
    <div class="zp-chips"><span>NVENC NATIVE</span><span>2560×1600</span><span>Display fps</span><span>Audio on</span></div>
    <div class="zp-strip" data-strip><div class="segs">${'<i></i>'.repeat(6)}</div><div class="pick"></div><div class="now"></div></div>
    <div class="zp-strip-l"><span>−1:00:00</span><span>Now</span></div>
    <div class="zp-durs" data-durs><div class="pill"></div><span class="on">15s</span><span>30s</span><span>1m</span><span>2m</span><span>5m</span><span>Custom</span></div>
    <div class="zp-save" data-save><span data-savet>Save last 15s</span><kbd>Alt+C</kbd></div>
    <div class="zp-tiles"><div><small>SAVED TODAY</small><b data-saved>0</b><span data-saveds>No clips yet</span></div><div><small>RECORDER CPU</small><b data-cpu>1.4%</b><span>of all cores</span></div><div><small>MEMORY</small><b>89 MB</b><span>engine + encoder</span></div></div>
  </div>
  <div class="zp-audio">
    <div class="zp-row"><b>Clip audio</b><span>Only changes what clips hear</span></div>
    <div class="zp-app" data-app="0"><span class="ic" style="background:#5865F2">D</span><span>Discord</span><span class="sl" style="--v:50%"><i></i><u></u></span><span class="v">100%</span>${I('vol')}</div>
    <div class="zp-app" data-app="1"><span class="ic" style="background:#1DB954">S</span><span>Spotify</span><span class="sl" style="--v:50%"><i></i><u></u></span><span class="v">100%</span>${I('vol')}</div>
    <div class="zp-app" data-app="2"><span class="ic" style="background:#1B2838">S</span><span>steam</span><span class="sl" style="--v:50%"><i></i><u></u></span><span class="v">100%</span>${I('vol')}</div>
  </div></div>`;
}
$('#hero-panel').innerHTML = panelHTML('hp');
$('#rp-fit').innerHTML = panelHTML('rp') + `<div class="rp-keys" id="rp-keys"><span class="key" id="k-alt">Alt</span>+<span class="key" id="k-c">C</span></div><div class="rp-note hand" id="rp-note1" style="left:-236px;top:166px">an hour of history →</div><div class="rp-note hand" id="rp-note2" style="left:-244px;top:222px">two minutes, please →</div>`;

/* ---------- The editor, rebuilt from EditorChrome.cs ---------- */
const PX = 128, LANE0 = 0; // pixels per second on the timeline
const bin = [['hero', 'hero.mp4', '0:06'], ['fighter', 'fighter.mp4', '0:05'], ['golf', 'golf.mp4', '0:05'], ['mech', 'mech.mp4', '0:05']];
$('#ed-fit').innerHTML = `<div class="zc ze" id="ze">
  <div class="ze-top"><span class="brand"><span class="orb"></span>ZenithClipper</span><span class="menu"><span>File</span><span>Edit</span><span>Sequence</span><span>Graphics</span><span>Captions</span><span>Window</span></span>
    <span class="doc"><b style="font-weight:500">Untitled</b><span id="ze-dirty">Saved</span></span>
    <span class="right">${I('undo')}${I('redo')}<span class="vsep"></span><span class="ze-export" id="ze-export">${I('upload')}Export</span><span class="ze-win"><span>${I('min')}</span><span>${I('sq')}</span><span>${I('x')}</span></span></span></div>
  <div class="ze-main">
    <div class="ze-left">
      <div class="ze-tabs"><span class="on">Media</span><span>Titles</span><span>Captions</span><span>Effects</span><span class="r" style="color:var(--soft)">${I('plus')}</span></div>
      <div class="ze-search"><div>Search clips</div><div class="sort">Added ${I('down')}</div><div style="width:30px;justify-content:center;color:var(--soft)">${I('list')}</div></div>
      <div class="ze-bin">${bin.map(([k, n, d], i) => `<div class="ze-card${i === 0 ? ' sel' : ''}" data-card="${i}"><div class="th"><video src="media/${k}.mp4" poster="media/${k}.jpg" muted loop playsinline preload="none"></video><span class="d">${d}</span></div><b>${n}</b><small>Video · 1280×720</small></div>`).join('')}</div>
      <div class="ze-add">${I('plus')}Add to timeline</div>
      <div class="ze-foot">4 clips · originals stay untouched</div>
    </div>
    <div class="ze-center">
      <div class="ze-tabs"><span style="color:var(--faint)">Source</span><span class="on">Program</span><span>Sequence 1</span><span class="r">1280×720 · 30 fps</span></div>
      <div class="ze-mon"><div class="frame"><video id="mon-hero" src="media/hero.mp4" muted loop playsinline preload="none" style="opacity:0"></video><video id="mon-fighter" src="media/fighter.mp4" muted loop playsinline preload="none" style="opacity:0"></video>
        <div class="empty" id="mon-empty"><i>${I('scissors')}</i><b>Make your first cut</b><span>Add a clip to the timeline<br>to preview your edit here.</span><em>Import media</em></div>
        <div class="ze-gg" id="ze-gg">GG</div></div></div>
      <div class="ze-trans"><span class="ze-tc" id="ze-tc">00:00:00.000</span><span id="ze-total">/ 00:00:00.000</span><span class="ze-btns">${I('skipb')}${I('stepb')}<span class="ze-play">${I('play')}</span>${I('stepf')}${I('skipf')}</span><span style="color:var(--soft);display:flex;gap:14px;align-items:center"><span style="padding:4px 10px;border:1px solid var(--line);border-radius:6px;display:flex;gap:18px">Fit ${I('down')}</span>${I('full')}</span></div>
    </div>
    <div class="ze-right">
      <div class="ze-tabs"><span class="on">Inspector</span><span>Mixer</span><span>Scopes</span><span>History</span></div>
      <div class="ze-insp" id="ze-insp"></div>
    </div>
  </div>
  <div class="ze-seqbar"><span>Sequence 1</span><span style="background:none;color:var(--soft);padding:7px 4px">${I('plus')}</span></div>
  <div class="ze-tools">
    <span class="t on" id="tool-sel">${I('pointer')}</span><span class="t">${I('ffwd')}</span><span class="t">${I('ripple')}</span><span class="t">${I('slip')}</span><span class="t" id="tool-cut">${I('scissors')}</span><span class="t">${I('split')}</span><span class="t">${I('clip')}</span><span class="t">${I('move')}</span><span class="t">${I('hand')}</span><span class="t">${I('zoom')}</span>
    <span class="vs"></span><span class="t">${I('scissors')}</span><span class="t">${I('trash')}</span><span class="t">${I('back')}</span><span class="t">${I('mark')}</span><span class="vs"></span>
    <span class="lbl snap"><i>${I('check')}</i>Snap</span><span class="vs"></span><span class="lbl">${I('film')}Video track</span><span class="lbl">${I('vol')}Audio track</span><span class="lbl" id="tool-title">${I('type')}Title</span><span class="t">${I('wand')}</span><span class="vs"></span><span class="lbl">${I('cc')}Auto captions</span>
    <span class="ze-zoom">${I('min')}<span class="bar"><u></u></span>${I('plus')}<span style="margin-left:8px">${I('full')}</span></span>
  </div>
  <div class="ze-tl" id="ze-tl">
    <div class="corner">TRACKS</div><div class="ze-ruler">${Array.from({ length: 12 }, (_, i) => `<span style="left:${i * PX}px">0:${String(i).padStart(2, '0')}</span>`).join('')}</div>
    <div class="hd" style="--c:#F5A524"><em>T</em><b>Titles</b>${I('eye')}${I('lock')}</div><div class="ze-lane" id="ln-t"><div class="ze-cl title" id="c-title" style="display:none"><div class="nm">GG</div><span class="edge"></span></div></div>
    <div class="hd" style="--c:#FF6B4A"><em>V1</em><b>Video 1</b>${I('eye')}${I('vol')}${I('lock')}</div><div class="ze-lane" id="ln-v">
      <div class="ze-cl" id="c-a" style="display:none"><div class="nm">hero.mp4</div><div class="fs" style="background-image:url(media/strip-hero.jpg)"></div><span class="edge"></span></div>
      <div class="ze-cl" id="c-b" style="display:none"><div class="nm">hero.mp4</div><div class="fs" style="background-image:url(media/strip-hero.jpg);background-position:-${2.7 * PX}px 0"></div><span class="edge"></span></div>
      <div class="ze-cl" id="c-f" style="display:none"><div class="nm">fighter.mp4</div><div class="fs" style="background-image:url(media/strip-fighter.jpg)"></div><span class="edge"></span></div>
      <div class="ze-cut" id="ze-cut"></div></div>
    <div class="hd" style="--c:#9A9EA8"><em>A1</em><b>Audio 1</b>${I('head')}${I('vol')}${I('lock')}</div><div class="ze-lane"></div>
    <div class="corner" style="border-bottom:0"></div><div></div>
    <div class="ze-ph" id="ze-ph" style="left:180px"></div>
  </div>
  <div class="ze-status"><i></i><span id="ze-msg">Imported 4 file(s). Add to timeline to create linked video and audio tracks.</span><span class="r">Space play · J K L shuttle · Ctrl+K cut · Ctrl+D dissolve</span></div>
  <div class="ze-ghost" id="ze-ghost">${I('play')}hero.mp4</div>
  <div class="ed-note hand" id="n-drag" style="left:300px;top:150px"><svg width="70" height="30" viewBox="0 0 70 30"><path d="M68 4C48 2 22 6 6 24M6 24l2-10M6 24l10-2"/></svg>drag it in</div>
  <div class="ed-note hand" id="n-cut" style="left:560px;top:612px">snip<svg width="54" height="26" viewBox="0 0 54 26"><path d="M2 4c18 0 32 6 46 18M48 22l-10-1M48 22l-1-10"/></svg></div>
  <div class="ed-note hand" id="n-trim" style="left:880px;top:600px"><svg width="54" height="26" viewBox="0 0 54 26"><path d="M52 4C34 4 20 10 6 22M6 22l10-1M6 22l1-10"/></svg>drag the edge</div>
  <div class="ed-note hand" id="n-dbl" style="left:300px;top:300px"><svg width="60" height="24" viewBox="0 0 60 24"><path d="M58 20C40 20 20 14 4 4M4 4l10 1M4 4l2 10"/></svg>double-click</div>
  <div class="ed-note hand" id="n-gg" style="left:1040px;top:120px"><svg width="60" height="24" viewBox="0 0 60 24"><path d="M58 4C40 6 22 12 6 20M6 20l9-5M6 20l10 2"/></svg>just type it</div>
</div>`;

/* ---------- Export dialog, rebuilt from the real dialog ---------- */
$('#ex-dlg').innerHTML = `<div class="zc zx"><h4>Export</h4>
  <div class="r">Preset<div><span id="ex-preset">Match sequence · H.264 high quality</span>${I('down')}</div></div>
  <div class="r">Range<div>Entire sequence${I('down')}</div></div><div class="r">Captions<div>Burn into video${I('down')}</div></div>
  <div class="r">Audio bitrate<div>Preset default${I('down')}</div></div><div class="r">Sample rate<div>48 kHz${I('down')}</div></div>
  <p id="ex-desc">Same size as the sequence. Visually lossless for most footage.<br>1280×720 · 00:00:10.000 long</p>
  <div class="b"><span>Cancel</span><span>Add to queue</span><span>Export…</span></div></div>`;
const presets = [['Match sequence · H.264 high quality', 'Same size as the sequence. Visually lossless for most footage.'], ['Discord · 10 MB', 'Fits Discord\'s free upload limit.'], ['YouTube 4K · H.265', 'Upscaled for YouTube\'s best quality tier.'], ['Animated GIF · 480p', 'For the group chat.'], ['Smallest file · AV1', 'Tiny files, great quality.'], ['Audio only · FLAC', 'Just the sound, lossless.']];
let pi = 0; setInterval(() => { if (reduce) return; pi = (pi + 1) % presets.length; $('#ex-preset').textContent = presets[pi][0]; $('#ex-desc').innerHTML = presets[pi][1] + '<br>1280×720 · 00:00:10.000 long'; }, 1800);
const fmts = ['MP4', 'H.265', 'AV1', 'MOV', 'MKV', 'WebM', 'GIF', 'ProRes', 'MP3', 'WAV', 'M4A', 'FLAC', 'NVENC', 'Discord', 'YouTube 4K'];
$('#mq-a').innerHTML = [...fmts, ...fmts].map((f) => `<span>${f}</span>`).join('');
$('#mq-b').innerHTML = [...fmts.slice().reverse(), ...fmts.slice().reverse()].map((f) => `<span>${f}</span>`).join('');

/* ---------- Games, comparison ---------- */
const games = [['hero', 'Overwatch 2'], ['fighter', 'Fighting game'], ['souls', 'Action RPG'], ['golf', 'Golf'], ['mech', 'Mech'], ['bf1', 'Battlefield 1'], ['race', 'Racing'], ['sim', 'Transport sim'], ['pool', 'Pool'], ['forest', 'Survival']];
$('#gg').innerHTML = games.map(([k, n]) => `<div class="g"><video src="media/${k}.mp4" poster="" muted loop playsinline preload="none"></video><span>${n}</span></div>`).join('');
const rows = ['Windows and Linux', 'NVIDIA, AMD and Intel GPUs', 'Built-in clip editor', 'No account needed', 'Under 100 MB of RAM'];
const vs = [['ZenithClipper', 'yyyyy', true], ['Medal', 'nyynn'], ['NVIDIA App', 'nnnyn'], ['OBS', 'yynyn'], ['SteelSeries Moments', 'nyyyq']];
$('#vs').innerHTML = vs.map(([n, v, us]) => `<div class="c${us ? ' us' : ''}"><h3>${us ? '<span class="orb-css"></span>' : ''}${n}</h3><ul>${rows.map((r, i) => `<li class="${v[i]}"><i>${v[i] === 'y' ? '✓' : v[i] === 'n' ? '–' : '?'}</i>${r}</li>`).join('')}</ul></div>`).join('');

/* ---------- Hero reel and background ---------- */
const reelItems = [['clutch.', 'hero'], ['combo.', 'fighter'], ['hole-in-one.', 'golf'], ['dogfight.', 'mech'], ['boss kill.', 'souls'], ['photo finish.', 'race'], ['trick shot.', 'pool']];
$('#reel').innerHTML = reelItems.map(([w]) => `<li>${w}</li>`).join('');
$('#hero-bg').innerHTML = reelItems.map(([, k], i) => `<video src="media/${k}.mp4" poster="media/${k === 'hero' ? 'hero' : k}.jpg" muted loop playsinline preload="${i === 0 ? 'auto' : 'none'}"${i === 0 ? ' class="on"' : ''}></video>`).join('');
const bgv = $$('#hero-bg video'); let ri = 0;
function reelTo(i) { ri = i; $('#reel').style.transform = `translateY(${-i * 1.15}em)`; bgv.forEach((v, k) => { v.classList.toggle('on', k === i); if (k === i) { v.preload = 'auto'; v.play().catch(() => {}); } }); }

/* ---------- Split text for reveals ---------- */
$$('.split').forEach((el) => {
  const walk = (node) => [...node.childNodes].forEach((c) => {
    if (c.nodeType === 3) { const f = document.createDocumentFragment(); c.textContent.split(/(\s+)/).forEach((t) => { if (!t) return; if (/^\s+$/.test(t)) f.appendChild(document.createTextNode(t)); else { const w = document.createElement('span'); w.className = 'w'; w.innerHTML = `<span>${t}</span>`; f.appendChild(w); } }); c.replaceWith(f); }
    else if (c.nodeType === 1 && c.tagName !== 'BR') walk(c);
  });
  walk(el);
});
$$('.giant').forEach((g) => { g.innerHTML = [...g.textContent].map((c) => `<span class="ch">${c}</span>`).join(''); });

/* ---------- Fit the fixed-size UIs into their boxes (they stay vector sharp) ---------- */
function fitAll() {
  const ed = $('#ed-fit'), box = $('#ed-box'); const s = Math.min(box.clientWidth / 1602, box.clientHeight / 902);
  ed.style.transform = `translateX(-50%) scale(${s})`; ed.style.transformOrigin = 'top center';
  const rp = $('#rp-fit'), small = innerWidth < 900; const rs = small ? Math.min(.8, (innerWidth - 40) / 402, (innerHeight * .52) / 640) : Math.min(1.25, (innerHeight * .86) / 640);
  rp.style.transform = `scale(${rs})`; rp.parentElement.style.width = 402 * rs + 'px'; rp.parentElement.style.height = 640 * rs + 'px';
  const hs = Math.min(1, innerHeight * .5 / 640, innerWidth * .8 / 400); $('#hero-panel').parentElement.style.scale = hs;
}
addEventListener('resize', fitAll); fitAll();

/* ---------- Live clocks in every panel ---------- */
const t0 = performance.now();
setInterval(() => { const s = 1271 + (performance.now() - t0) / 1000; const t = `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`; $$('[data-clock]').forEach((e) => e.textContent = t); $$('[data-mb]').forEach((e) => e.textContent = Math.round(611 + (s - 1271) * .48) + ' MB'); }, 250);
setInterval(() => $$('[data-cpu]').forEach((e) => e.textContent = (1.1 + Math.random() * .7).toFixed(1) + '%'), 1600);

/* ---------- Loader ---------- */
const lb = $('#ld-bar'); for (let i = 0; i < 20; i++) lb.appendChild(document.createElement('i'));
const lbi = $$('i', lb), first = bgv[0]; let shown = 0, target = .1;
const bump = (x) => target = Math.max(target, x);
first.addEventListener('loadedmetadata', () => bump(.6)); first.addEventListener('loadeddata', () => bump(1));
(document.fonts ? document.fonts.ready : Promise.resolve()).then(() => bump(1)); setTimeout(() => bump(1), 2200);
const lt0 = performance.now();
(function tick() {
  const minT = performance.now() - lt0 > (reduce ? 200 : 1500);
  shown += ((minT ? target : Math.min(target, .9)) - shown) * .09; if (target >= 1 && minT && shown > .995) shown = 1;
  const k = Math.round(shown * 20); lbi.forEach((b, i) => { b.classList.toggle('on', i < k); b.classList.toggle('head', i === k - 1); });
  $('#ld-n').textContent = String(Math.round(shown * 100)).padStart(3, '0') + '%';
  if (shown < 1) return requestAnimationFrame(tick);
  setTimeout(start, 200);
})();

function start() {
  $('#ld').classList.add('out'); document.body.classList.remove('lock');
  first.play().catch(() => {});
  if (!reduce) setInterval(() => { if (scrollY < innerHeight * 1.5) reelTo((ri + 1) % reelItems.length); }, 2400);
  if (!window.gsap) return;
  gsap.from('#g-back .ch', { yPercent: 110, opacity: 0, duration: 1.4, stagger: .06, ease: 'expo.out', delay: .4 });
  gsap.from('#g-front .ch', { yPercent: -60, opacity: 0, duration: 1.4, stagger: .06, ease: 'expo.out', delay: .6 });
  gsap.from('#hero-panel', { y: 120, opacity: 0, duration: 1.6, ease: 'expo.out', delay: .5 });
  gsap.from('.hero-tag', { y: 20, opacity: 0, duration: 1.2, ease: 'expo.out', delay: 1 });
}

/* ---------- Scroll story ---------- */
if (!window.gsap) return;
gsap.registerPlugin(ScrollTrigger);
if (window.Lenis && !reduce) {
  const lenis = new Lenis({ duration: 1.15 });
  lenis.on('scroll', ScrollTrigger.update); gsap.ticker.add((t) => lenis.raf(t * 1000)); gsap.ticker.lagSmoothing(0);
  $$('a[href^="#"]').forEach((a) => a.addEventListener('click', (e) => { const el = $(a.getAttribute('href')); if (el) { e.preventDefault(); lenis.scrollTo(el); } }));
}
const scene = (sel, fn) => ScrollTrigger.create({ trigger: sel, start: 'top top', end: 'bottom bottom', onUpdate: (s) => fn(s.progress), onRefresh: (s) => fn(s.progress) });
const vio = new IntersectionObserver((es) => es.forEach((e) => { const v = e.target; if (e.isIntersecting) { v.preload = 'auto'; v.play().catch(() => {}); } else v.pause(); }), { rootMargin: '-8% 0px' });
const playIn = (sel) => $$('video', $(sel)).forEach((v) => vio.observe(v));

// Hero: the giant words part, the panel lifts flat
gsap.timeline({ scrollTrigger: { trigger: '#hero', start: 'top top', end: 'bottom bottom', scrub: 1 } })
  .to('#g-back', { yPercent: -60, xPercent: -18, opacity: 0, ease: 'none' }, 0)
  .to('#g-front', { yPercent: 70, xPercent: 18, opacity: 0, ease: 'none' }, 0)
  .to('#hero-panel', { rotate: 0, ease: 'none' }, 0)
  .to('#hero-stage', innerWidth < 820 ? { opacity: 0, yPercent: -80, ease: 'none', duration: .3 } : { left: '72%', ease: 'none' }, 0)
  .to('.hero-tag', { opacity: 0, ease: 'none', duration: .2 }, 0)
  .fromTo('#hero-copy', { opacity: 0, y: 40 }, { opacity: 1, y: 0, ease: 'none', duration: .35 }, .3)
  .to('#hero-copy, #hero-stage', { opacity: 0, y: -60, ease: 'none', duration: .25 }, .85);

// The problem: notes drift in, then all fall into the orb
const pains = ['😵 Wait, was that recording?', '🎮 Forgot to hit record. Again.', '⏱️ It only kept 30 seconds.', '🔊 Discord is all over my clip.', '✂️ Now I need a whole editor too.', '🐢 My game drops frames when I record.', '📁 Where did that clip even go?', '🤷 My friends won\'t believe me.', '📦 Discord says the file is too big.'];
const chipBox = $('#chips');
const spots = [[-36, -36, -6], [32, -38, 5], [-42, 30, 4], [38, 28, -5], [-24, 40, -3], [20, 42, 6], [-2, -42, 2], [44, -8, -4], [-44, -6, 3]];
pains.forEach((t, i) => { const c = document.createElement('div'); c.className = 'chip'; const [em, ...rest] = t.split(' '); c.innerHTML = `<i>${em}</i>${rest.join(' ')}`; chipBox.appendChild(c); c.dataset.i = i; });
const chips = $$('.chip');
scene('#problem', (p) => {
  const tIn = ease(seg(p, 0, .14)), gather = ease(seg(p, .4, .66)), out = seg(p, .62, .7);
  $$('#pb-title .w > span').forEach((w, i) => { const k = ease(seg(p, i * .012, .1 + i * .012)); w.style.transform = `translateY(${(1 - k) * 110}%)`; });
  $('#pb-title').style.opacity = 1 - seg(p, .36, .46);
  chips.forEach((c, i) => {
    const [x, y, r] = spots[i]; const appear = ease(seg(p, .1 + i * .025, .22 + i * .025)); const drift = Math.sin(p * 9 + i) * 8;
    const cx = lerp(x * innerWidth / 100, 0, gather), cy = lerp(y * innerHeight / 100 + drift, 0, gather);
    c.style.transform = `translate(-50%,-50%) translate(${cx}px,${cy}px) rotate(${lerp(r, 0, gather)}deg) scale(${lerp(.9 + appear * .1, .1, gather)})`;
    c.style.left = '50%'; c.style.top = '50%'; c.style.opacity = appear * (1 - out);
  });
  const o = $('#pb-orb'); const ok = ease(seg(p, .55, .7)); o.style.opacity = ok; o.style.transform = `translateY(${-ok * 18}vh) scale(${.3 + ok * .7 + Math.sin(p * 40) * .02 * ok})`;
  $$('#pb-exists .w > span').forEach((w, i) => { const k = ease(seg(p, .7 + i * .025, .8 + i * .025)); w.style.transform = `translateY(${(1 - k) * 110}%)`; });
});

// Instant replay: four beats on the real panel
const rp = $('#rp'), beats = $$('.beat'), durs = $('[data-durs]', rp), pill = $('.pill', durs), dspans = $$('span', durs);
const dNames = ['15s', '30s', '1m', '2m'], dPick = [2, 4, 7, 12], flash = $('#flash');
let counted = false;
scene('#replay', (p) => {
  const b = Math.min(3, Math.floor(p * 4)); beats.forEach((el, i) => { const local = seg(p, i / 4, (i + 1) / 4); const vis = i === b ? Math.min(1, local * 6, (1 - local) * 6 + (i === 3 ? 9 : 0)) : 0; el.style.opacity = vis; el.style.transform = `translateY(${(1 - Math.min(1, local * 6)) * 30}px)`; });
  if (b === 0 && !counted) { counted = true; const e = $('[data-count]'); gsap.to({ v: 0 }, { v: 89, duration: 1.6, ease: 'expo.out', onUpdate() { e.textContent = Math.round(this.targets()[0].v); } }); }
  $('#rp-note1').style.opacity = b === 0 ? 1 : 0; $('#rp-note2').style.opacity = b === 1 ? 1 : 0;
  $('[data-strip] .segs', rp).style.setProperty('--buf', (38 + seg(p, 0, .25) * 50) + '%');
  const di = Math.min(3, Math.floor(seg(p, .27, .47) * 4)); pill.style.transform = `translateX(${di * 100}%)`; dspans.forEach((s, i) => s.classList.toggle('on', i === di));
  const saving = p > .6 && p < .645, saved = p >= .645;
  $('[data-savet]', rp).textContent = saving ? 'Saving…' : 'Save last ' + dNames[di];
  $('[data-strip] .pick', rp).style.setProperty('--pick', dPick[di] + '%');
  const kOn = p > .52 && p < .74; const keys = $('#rp-keys'); keys.style.opacity = kOn ? 1 : 0; keys.style.transform = kOn ? 'none' : 'translateY(20px)';
  $('#k-alt').classList.toggle('down', p > .575 && p < .66); $('#k-c').classList.toggle('down', p > .595 && p < .63);
  $('[data-save]', rp).classList.toggle('press', p > .595 && p < .63);
  flash.style.opacity = p > .6 && p < .63 ? (1 - (p - .6) / .03) * .35 : 0;
  $('[data-toast]', rp).classList.toggle('on', saved);
  $('[data-saved]', rp).textContent = saved ? '1' : '0'; $('[data-saveds]', rp).textContent = saved ? '58 MB' : 'No clips yet';
  const sp = ease(seg(p, .78, .86)), dc = ease(seg(p, .88, .97));
  const apps = $$('[data-app]', rp);
  const set = (row, v, off) => { $('.sl', row).style.setProperty('--v', v + '%'); $('.v', row).textContent = off ? 'Off' : Math.round(v * 2) + '%'; row.classList.toggle('off', off); row.lastElementChild.outerHTML = I(off ? 'mute' : 'vol'); };
  set(apps[1], 50 - sp * 50, sp >= 1); set(apps[0], 50 - dc * 30, false);
});

// The editor: six beats on the rebuilt editor
const titles = $$('#ed-titles h2'), ghost = $('#ze-ghost'), ph = $('#ze-ph'), cA = $('#c-a'), cB = $('#c-b'), cF = $('#c-f'), cT = $('#c-title');
const monH = $('#mon-hero'), monF = $('#mon-fighter'), notes = { drag: $('#n-drag'), cut: $('#n-cut'), trim: $('#n-trim'), dbl: $('#n-dbl'), gg: $('#n-gg') };
const place = (el, a, b) => { el.style.display = b > a ? '' : 'none'; el.style.left = a * PX + 'px'; el.style.width = (b - a) * PX + 'px'; };
const tc = (s) => `00:00:${String(Math.floor(s)).padStart(2, '0')}.${String(Math.floor((s % 1) * 1000)).padStart(3, '0')}`;
function insp(html) { const el = $('#ze-insp'); if (el._k !== html) { el._k = html; el.innerHTML = html; } }
const inspEmpty = `<div class="empty"><i>${I('move')}</i><b>Fine-tune your edit</b>Select a timeline clip to adjust timing, motion, sound and effects.</div>`;
const inspClip = (name, sub, start, dur) => `<div class="ze-clip"><i>${I(name === 'GG' ? 'type' : 'film')}</i><div><b>${name}</b><small>${sub}</small></div></div><div class="ze-seg"><span class="on">Video</span><span>Color</span><span>Audio</span><span>Effects</span></div>
  <div class="ze-prop" style="margin-top:8px">Source info ${I('right')}</div><div class="ze-prop">Timing ${I('down')}</div><div class="ze-prop">Timeline start<b>${start}</b></div><div class="ze-prop">Duration<b>${dur}</b></div><div class="ze-prop">Speed<b>100</b></div>`;
scene('#editor', (p) => {
  const st = Math.min(5, Math.floor(p * 6)); titles.forEach((h, i) => { const l = seg(p, i / 6, (i + 1) / 6); h.style.opacity = i === st ? Math.min(1, l * 8, i === 5 ? 1 : (1 - l) * 8) : 0; h.style.transform = `translateY(${(1 - Math.min(1, l * 8)) * 24}px)`; });
  $('#ed-step').innerHTML = `<b>0${st + 1}</b> / 06`;
  const ze = $('#ze'), card0 = $('[data-card="0"]', ze), card1 = $('[data-card="1"]', ze);
  // 1. drag hero.mp4 from the bin onto Video 1
  const d = ease(seg(p, .02, .13)), dropped = p >= .13;
  const g0 = [card0.offsetLeft + 30, card0.offsetTop + 100], laneTop = 44 + 455 + 33 + 40 + 26 + 52 + 10;
  ghost.style.opacity = p > .015 && !dropped ? 1 : 0; ghost.style.left = lerp(g0[0], 190, d) + 'px'; ghost.style.top = lerp(g0[1], laneTop, d) - Math.sin(d * Math.PI) * 120 + 'px';
  card0.classList.toggle('sel', p < .48); card1.classList.toggle('sel', p >= .48 && p < .62);
  notes.drag.style.opacity = p > .01 && p < .14 ? 1 : 0;
  // 2. playhead to 2.7 s, Ctrl+K
  const phTo = ease(seg(p, .18, .25)) * 2.7, split = p >= .27, trimK = ease(seg(p, .36, .45)), bEnd = 6 - trimK * 1.0;
  $('#tool-cut').classList.toggle('hot', p > .2 && p < .32); notes.cut.style.opacity = p > .22 && p < .32 ? 1 : 0;
  const cut = $('#ze-cut'); cut.style.left = 2.7 * PX + 'px'; cut.style.opacity = p > .265 && p < .29 ? 1 : 0;
  // 3. trim the end of the second half
  notes.trim.style.opacity = p > .35 && p < .47 ? 1 : 0;
  // 4. double-click fighter.mp4: it lands after everything
  const added = p >= .53; notes.dbl.style.opacity = p > .49 && p < .6 ? 1 : 0;
  // 5. title
  const ti = ease(seg(p, .66, .72));
  notes.gg.style.opacity = p > .68 && p < .78 ? 1 : 0; $('#tool-title').style.color = p > .64 && p < .74 ? 'var(--acc)' : '';
  // clips
  if (!dropped) { [cA, cB, cF].forEach((c) => c.style.display = 'none'); }
  else if (!split) { place(cA, 0, 6); cB.style.display = 'none'; }
  else { place(cA, 0, 2.7); place(cB, 2.7, bEnd); }
  cB.classList.toggle('trim', p > .36 && p < .46); cB.classList.toggle('sel', split && p < .48); cA.classList.toggle('sel', dropped && !split);
  if (added) { place(cF, bEnd, bEnd + 5); cF.style.transform = `translateY(${(1 - ease(seg(p, .53, .57))) * -30}px)`; cF.classList.toggle('sel', p < .62); } else cF.style.display = 'none';
  if (p >= .66) { place(cT, .8, .8 + 3 * ti); cT.classList.toggle('sel', p < .78); } else cT.style.display = 'none';
  // playhead + playback
  const end = bEnd + 5; let t = dropped ? (p < .27 ? phTo : p < .5 ? 2.7 : p < .62 ? bEnd : p < .78 ? 1.6 : ease(seg(p, .79, .98)) * end) : 0;
  ph.style.left = 180 + t * PX + 'px'; $('#ze-tc').textContent = tc(t); $('#ze-total').textContent = '/ ' + tc(dropped ? (added ? end : split ? bEnd : 6) : 0);
  const showF = added && t >= bEnd; monH.style.opacity = dropped && !showF ? 1 : 0; monF.style.opacity = showF ? 1 : 0; $('#mon-empty').style.opacity = dropped ? 0 : 1;
  const gg = $('#ze-gg'), ggOn = p >= .67 && t >= .8 && t <= 3.8; gg.style.setProperty('--o', ggOn ? 1 : 0); gg.style.setProperty('--s', ggOn ? 1 : .8); gg.style.outlineColor = p < .78 ? 'var(--acc)' : 'transparent';
  $('#ze-export').classList.toggle('glow', p > .96); $('#ze-dirty').textContent = dropped ? 'Unsaved changes' : 'Saved';
  $('#tool-sel').classList.toggle('on', !(p > .2 && p < .32));
  if (!dropped) insp(inspEmpty);
  else if (p >= .66 && p < .78) insp(inspClip('GG', 'Titles · 3 s · 90 frames', '0.800', '3'));
  else if (added && p < .62) insp(inspClip('fighter.mp4', 'Video 1 · 5 s · 150 frames', bEnd.toFixed(3), '5'));
  else if (split) insp(inspClip('hero.mp4', `Video 1 · ${(bEnd - 2.7).toFixed(1)} s`, '2.700', (bEnd - 2.7).toFixed(1)));
  else insp(inspClip('hero.mp4', 'Video 1 · 6 s · 180 frames', '0.000', '6'));
  $('#ze-msg').textContent = !dropped ? 'Imported 4 file(s). Add to timeline to create linked video and audio tracks.' : p < .27 ? 'Added hero.mp4 to Video 1.' : p < .36 ? 'Split at 00:00:02.700.' : p < .5 ? 'Trimmed hero.mp4.' : p < .66 ? 'Added fighter.mp4 after the last clip.' : p < .79 ? 'Created title "GG".' : 'Playing.';
});
['#hero', '#editor', '#games'].forEach(playIn);
ScrollTrigger.create({ trigger: '#editor', start: 'top bottom', end: 'bottom top', onToggle: (s) => { [monH, monF, ...$$('.ze-card video')].forEach((v) => { if (s.isActive) { v.preload = 'auto'; v.play().catch(() => {}); } else v.pause(); }); } });

// Export marquee, reveals, proof
gsap.to('#mq-a', { xPercent: -50, ease: 'none', scrollTrigger: { trigger: '#export', start: 'top bottom', end: 'bottom top', scrub: true } });
gsap.fromTo('#mq-b', { xPercent: -50 }, { xPercent: 0, ease: 'none', scrollTrigger: { trigger: '#export', start: 'top bottom', end: 'bottom top', scrub: true } });
gsap.from('#ex-dlg', { y: 100, opacity: 0, duration: 1.4, ease: 'expo.out', scrollTrigger: { trigger: '#export', start: 'top 60%', toggleActions: 'play none none reverse' } });
$$('#export .split, #games .split, #proof .split, #get .split').forEach((h) => gsap.from($$('.w > span', h), { yPercent: 110, duration: 1.2, stagger: .05, ease: 'expo.out', scrollTrigger: { trigger: h, start: 'top 85%', toggleActions: 'play none none reverse' } }));
ScrollTrigger.batch('#gg .g', { start: 'top 92%', onEnter: (b) => gsap.from(b, { y: 60, opacity: 0, scale: .96, stagger: .07, duration: 1.1, ease: 'expo.out' }) });
ScrollTrigger.create({ trigger: '.big-mb', start: 'top 80%', once: true, onEnter: () => gsap.to({ v: 0 }, { v: 89, duration: 2, ease: 'expo.out', onUpdate() { $('#mb').textContent = Math.round(this.targets()[0].v); } }) });
ScrollTrigger.create({ trigger: '#bars', start: 'top 80%', once: true, onEnter: () => $$('#bars .f').forEach((f, i) => gsap.to(f, { width: getComputedStyle(f.closest('.bar')).getPropertyValue('--w'), duration: 1.6, delay: i * .12, ease: 'expo.out' })) });
ScrollTrigger.batch('#vs .c', { start: 'top 90%', onEnter: (b) => gsap.from(b, { y: 60, opacity: 0, rotate: 2, stagger: .08, duration: 1, ease: 'back.out(1.6)' }) });
gsap.fromTo('#get .giant', { xPercent: 10 }, { xPercent: -10, ease: 'none', scrollTrigger: { trigger: '#get', start: 'top bottom', end: 'bottom top', scrub: 1 } });
addEventListener('load', () => { fitAll(); ScrollTrigger.refresh(); });
})();
