namespace SGDB.Services;

internal static class DeckCompanionHtml
{
    public const string Page = """
<!DOCTYPE html>
<html lang="pt-BR">
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no"/>
<meta name="apple-mobile-web-app-capable" content="yes"/>
<meta name="apple-mobile-web-app-title" content="SGDB Decks"/>
<meta name="mobile-web-app-capable" content="yes"/>
<meta name="theme-color" content="#1e3a5f"/>
<link rel="manifest" href="/manifest.webmanifest"/>
<title>Mesas — SGDB</title>
<style>
  :root {
    --bg:#f5f5f5; --card:#fff; --line:#e2e8f0; --txt:#0f172a; --muted:#64748b;
    --green:#2E7D32; --yellow:#C4A035; --gray:#5C5C5C; --blue:#1D4ED8;
    --header:#1e3a5f; --ok:#16a34a;
  }
  * { box-sizing: border-box; }
  body { margin:0; font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; background:var(--bg); color:var(--txt); }
  header { padding:14px 16px; background:var(--header); color:#fff; position:sticky; top:0; z-index:5; }
  header h1 { margin:0; font-size:17px; font-weight:700; }
  header .sub { opacity:.85; font-size:12px; margin-top:2px; }
  main { padding:12px 12px 88px; max-width:720px; margin:0 auto; }
  .card { background:var(--card); border:1px solid var(--line); border-radius:12px; padding:12px; margin-bottom:12px; }
  .card h2 { margin:0 0 8px; font-size:14px; }
  label { display:block; font-size:12px; color:var(--muted); margin-bottom:4px; }
  input, button { font: inherit; }
  input { width:100%; padding:12px; border-radius:10px; border:1px solid #cbd5e1; background:#fff; color:var(--txt); }
  button { border:0; border-radius:10px; padding:12px 14px; font-weight:600; cursor:pointer; }
  .btn { background:#38bdf8; color:#082f49; width:100%; }
  .btn-secondary { background:#e2e8f0; color:#0f172a; }
  .btn-ok { background:var(--ok); color:#fff; }
  .btn-warn { background:#C4A035; color:#fff; width:100%; font-size:15px; padding:14px; }
  .row { display:flex; gap:8px; }
  .row > * { flex:1; }
  .muted { color:var(--muted); font-size:12px; }
  .err { color:#991b1b; background:#fee2e2; border-radius:8px; padding:8px 10px; margin:8px 0; font-size:13px; }
  .okmsg { color:#14532d; background:#dcfce7; border-radius:8px; padding:8px 10px; margin:8px 0; font-size:13px; }
  .item { display:flex; justify-content:space-between; gap:8px; padding:10px 0; border-bottom:1px solid var(--line); font-size:13px; align-items:center; }
  .item:last-child { border-bottom:0; }
  .item-actions { display:flex; gap:6px; align-items:center; flex-shrink:0; }
  .item-actions button { width:auto; padding:8px 10px; font-size:13px; min-width:36px; }
  .btn-danger { background:#fee2e2; color:#991b1b; }
  .qty-val { font-weight:700; min-width:28px; text-align:center; }
  .suggest { background:#fff; border:1px solid var(--line); border-radius:10px; max-height:220px; overflow:auto; margin-top:6px; }
  .suggest div { padding:10px 12px; border-bottom:1px solid var(--line); cursor:pointer; }
  .suggest div:active, .suggest div:hover { background:#f1f5f9; }
  .topbar { display:flex; justify-content:space-between; align-items:center; gap:8px; margin-bottom:10px; }
  .hidden { display:none !important; }
  .tip { background:#eff6ff; border:1px solid #bfdbfe; border-radius:10px; padding:10px; margin-bottom:10px; font-size:12px; color:#1e3a5f; line-height:1.45; }
  .legend { display:flex; flex-wrap:wrap; gap:6px; margin:8px 0 12px; }
  .pill { font-size:11px; border-radius:999px; padding:4px 10px; font-weight:600; display:inline-flex; align-items:center; gap:6px; }
  .pill i { width:8px; height:8px; border-radius:50%; display:inline-block; }
  .pill.g { background:#dcfce7; color:#14532d; } .pill.g i { background:var(--green); }
  .pill.y { background:#fef3c7; color:#854d0e; } .pill.y i { background:var(--yellow); }
  .pill.z { background:#e5e7eb; color:#374151; } .pill.z i { background:var(--gray); }
  .sec-title { font-size:14px; font-weight:700; margin:14px 2px 8px; color:#1e293b; display:flex; justify-content:space-between; }
  .mesa-grid { display:grid; grid-template-columns:repeat(4, 1fr); gap:8px; }
  @media (min-width:520px){ .mesa-grid { grid-template-columns:repeat(6, 1fr); } }
  .mesa {
    aspect-ratio:1/1; border-radius:8px; color:#fff; padding:8px 6px;
    display:flex; flex-direction:column; align-items:center; justify-content:center;
    text-align:center; cursor:pointer; user-select:none; position:relative;
    box-shadow:0 1px 2px rgba(0,0,0,.08);
  }
  .mesa:active { transform:scale(.97); }
  .mesa.free { background:var(--gray); }
  .mesa.occ { background:var(--green); }
  .mesa.pre { background:var(--yellow); }
  .mesa.bal { background:var(--blue); }
  .mesa .ico { font-size:14px; line-height:1; opacity:.95; }
  .mesa .abrir { font-size:10px; font-weight:700; letter-spacing:.02em; margin-top:2px; }
  .mesa .num { font-size:22px; font-weight:800; line-height:1.1; margin-top:2px; max-width:100%; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .mesa .cli { font-size:10px; opacity:.95; margin-top:2px; max-width:100%; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .mesa .ft { font-size:9px; font-weight:700; opacity:.92; margin-top:3px; max-width:100%; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .mesa .lock { position:absolute; top:5px; left:6px; font-size:11px; }
  .modal-bg { position:fixed; inset:0; background:rgba(15,23,42,.45); z-index:20; display:flex; align-items:flex-end; justify-content:center; padding:16px; }
  .modal { background:#fff; border-radius:16px 16px 12px 12px; padding:16px; width:100%; max-width:420px; }
  .modal h3 { margin:0 0 10px; font-size:16px; }
</style>
</head>
<body>
<header>
  <h1 id="storeTitle">Mesas</h1>
  <div class="sub" id="storeSub">SGDB — comandas pelo celular</div>
</header>
<main>
  <section id="viewLogin" class="card">
    <h2>Entrar</h2>
    <p class="muted">Digite o PIN que aparece no computador.</p>
    <label>PIN</label>
    <input id="pinInput" type="password" inputmode="numeric" maxlength="8" placeholder="••••" autocomplete="one-time-code"/>
    <div id="loginErr" class="err hidden"></div>
    <div style="height:10px"></div>
    <button class="btn" id="btnLogin">Entrar</button>
    <div class="tip" style="margin-top:12px;margin-bottom:0">
      <strong>Não precisa do QR toda vez.</strong><br/>
      Depois de abrir uma vez: use <strong>Adicionar à tela inicial</strong> / Favoritos.
    </div>
  </section>

  <section id="viewList" class="hidden">
    <div class="topbar">
      <div>
        <div style="font-weight:700">Mapa de mesas</div>
        <div class="muted" id="listMeta"></div>
      </div>
      <button class="btn-secondary" id="btnRefresh" style="width:auto">Atualizar</button>
    </div>
    <div class="legend">
      <span class="pill g"><i></i>Em andamento</span>
      <span class="pill y"><i></i>Pré-conta</span>
      <span class="pill z"><i></i>Livre</span>
    </div>
    <div class="tip" id="homeTip">
      <strong>Toque na mesa:</strong> cinza abre conta · verde/amarelo lança itens.
      A mesa fica no mesmo lugar do mapa.
      <div style="margin-top:8px">
        <button class="btn-secondary" id="btnDismissTip" type="button" style="width:auto;padding:8px 12px;font-size:12px">Entendi</button>
      </div>
    </div>
    <div class="sec-title"><span>Mesas</span><span class="muted" id="mapStats"></span></div>
    <div class="mesa-grid" id="mapGrid"></div>
    <div id="balcaoWrap" class="hidden">
      <div class="sec-title"><span>Balcão / Avulso</span><span class="muted" id="balcaoStats"></span></div>
      <div class="mesa-grid" id="balcaoGrid"></div>
    </div>
    <div class="card" style="margin-top:14px">
      <label>Abrir comanda avulsa (sem nº de mesa)</label>
      <div class="row">
        <input id="newName" placeholder="Ex.: Hugo / Fernando" maxlength="80"/>
        <button class="btn-ok" id="btnCreate" style="flex:0 0 auto;width:auto">Abrir</button>
      </div>
      <div id="createErr" class="err hidden"></div>
    </div>
  </section>

  <section id="viewDeck" class="hidden">
    <div class="topbar">
      <div class="row" style="flex:0 0 auto; width:auto; gap:6px">
        <button class="btn-secondary" id="btnBack" style="width:auto;padding:10px 12px">← Mesas</button>
        <button class="btn-ok" id="btnNewFromDeck" style="width:auto;padding:10px 12px">Nova</button>
      </div>
      <div style="text-align:right; min-width:0">
        <div style="font-weight:700" id="deckName">—</div>
        <div class="muted" id="deckTotal">Total —</div>
      </div>
    </div>
    <div class="card">
      <button class="btn-warn" id="btnPreConta" type="button">Solicitar Pré-Conta (Fechar)</button>
      <p class="muted" style="margin:8px 0 0">Avisa o caixa e deixa a mesa amarela. A impressão sai no PC do caixa.</p>
      <div id="preMsg" class="okmsg hidden"></div>
      <div id="preErr" class="err hidden"></div>
    </div>
    <div class="card">
      <label>Buscar produto</label>
      <input id="prodSearch" placeholder="Código, barras ou nome" autocomplete="off"/>
      <div id="suggest" class="suggest hidden"></div>
      <div style="height:8px"></div>
      <div class="row">
        <div>
          <label>Qtd</label>
          <input id="prodQty" class="qty" type="number" inputmode="decimal" value="1" min="0.001" step="1"/>
        </div>
        <div style="align-self:flex-end">
          <button class="btn" id="btnAdd">Lançar</button>
        </div>
      </div>
      <div id="addMsg" class="okmsg hidden"></div>
      <div id="addErr" class="err hidden"></div>
    </div>
    <div class="card">
      <h2>Itens</h2>
      <div id="itemList"></div>
    </div>
  </section>
</main>

<div id="openModal" class="modal-bg hidden">
  <div class="modal">
    <h3 id="openModalTitle">Abrir mesa</h3>
    <label>Nome (opcional — cliente)</label>
    <input id="openClientName" placeholder="Ex.: Fernando" maxlength="80"/>
    <div id="openErr" class="err hidden"></div>
    <div class="row" style="margin-top:12px">
      <button class="btn-secondary" id="btnOpenCancel">Cancelar</button>
      <button class="btn-ok" id="btnOpenConfirm">Abrir</button>
    </div>
  </div>
</div>

<div id="modeModal" class="modal-bg hidden">
  <div class="modal">
    <h3 id="modeProductName">Produto</h3>
    <p class="muted" style="margin:0 0 12px">Escolha a modalidade</p>
    <button class="btn-ok" id="btnModeAvulso" type="button" style="width:100%;margin-bottom:8px;min-height:64px;font-size:16px">
      AVULSO<br/><span id="modeAvulsoPrice" style="font-weight:500;font-size:15px"></span>
    </button>
    <button class="btn" id="btnModeMaco" type="button" style="width:100%;min-height:64px;font-size:16px;background:#1e3a5f;color:#fff">
      MAÇO<br/><span id="modeMacoPrice" style="font-weight:500;font-size:15px"></span>
    </button>
    <button class="btn-secondary" id="btnModeCancel" type="button" style="width:100%;margin-top:10px">Cancelar</button>
  </div>
</div>

<script>
const state = {
  pin: localStorage.getItem('deckPin') || '',
  deckId: null,
  selectedProduct: null,
  pendingTable: null,
  pendingModeProduct: null
};

function $(id){ return document.getElementById(id); }
function show(id){
  ['viewLogin','viewList','viewDeck'].forEach(v => $(v).classList.toggle('hidden', v !== id));
}
function headers(){
  return { 'Content-Type':'application/json', 'X-Deck-Pin': state.pin };
}
async function api(path, opts={}){
  const res = await fetch(path, {
    ...opts,
    headers: { ...(opts.headers||{}), ...headers() }
  });
  const data = await res.json().catch(() => ({ error: 'Resposta inválida' }));
  if (!res.ok) throw new Error(data.error || ('Erro ' + res.status));
  return data;
}

async function login(){
  $('loginErr').classList.add('hidden');
  const pin = ($('pinInput').value || '').trim();
  if (pin.length < 4) { showErr('loginErr','Informe o PIN.'); return; }
  try {
    const data = await fetch('/api/login', {
      method:'POST', headers:{'Content-Type':'application/json'},
      body: JSON.stringify({ pin })
    }).then(r => r.json().then(j => ({ok:r.ok, j})));
    if (!data.ok) throw new Error(data.j.error || 'PIN incorreto');
    state.pin = pin;
    localStorage.setItem('deckPin', pin);
    $('storeTitle').textContent = data.j.store || 'Mesas';
    await loadDecks();
    show('viewList');
  } catch(e) {
    showErr('loginErr', e.message);
  }
}

function showErr(id, msg){ const el=$(id); el.textContent=msg; el.classList.remove('hidden'); }
function showOk(id, msg){ const el=$(id); el.textContent=msg; el.classList.remove('hidden'); setTimeout(()=>el.classList.add('hidden'), 1800); }

function tileHtml(c){
  let cls = 'mesa free';
  if (c.balcao) cls = c.preconta ? 'mesa pre' : 'mesa bal';
  else if (c.preconta) cls = 'mesa pre';
  else if (!c.free) cls = 'mesa occ';

  const lock = c.preconta ? '<span class="lock">🔒</span>' : '';
  const abrir = c.free ? '<div class="abrir">ABRIR</div>' : (c.balcao ? '<div class="abrir">BALCÃO</div>' : '');
  const num = escapeHtml(c.balcao ? (c.title || c.number || 'AV') : (c.number || ''));
  const cli = (!c.free && c.name) ? `<div class="cli">${escapeHtml(c.name)}</div>` : '';
  const ft = (!c.free && c.footer) ? `<div class="ft">${escapeHtml(c.footer)}</div>` : '';
  const idAttr = c.id ? `data-id="${c.id}"` : '';
  const mesaAttr = c.tableNumber ? `data-mesa="${c.tableNumber}"` : '';
  return `<div class="${cls}" ${idAttr} ${mesaAttr} data-free="${c.free?1:0}">
    ${lock}<div class="ico">🪑</div>${abrir}<div class="num">${num}</div>${cli}${ft}
  </div>`;
}

function bindTiles(root){
  root.querySelectorAll('.mesa').forEach(el => {
    el.onclick = () => {
      if (el.dataset.free === '1') {
        const n = +el.dataset.mesa;
        promptOpenMesa(n);
      } else if (el.dataset.id) {
        openDeck(+el.dataset.id);
      }
    };
  });
}

async function loadDecks(){
  const data = await api('/api/decks');
  const occ = data.occupied || 0;
  const free = data.free || 0;
  $('listMeta').textContent = (data.tableCount || 0) + ' mesas no mapa';
  $('mapStats').textContent = occ + ' aberta(s) · ' + free + ' livre(s)';
  const map = data.map || [];
  $('mapGrid').innerHTML = map.map(tileHtml).join('');
  bindTiles($('mapGrid'));

  const bal = data.balcao || [];
  if (bal.length) {
    $('balcaoWrap').classList.remove('hidden');
    $('balcaoStats').textContent = bal.length + '';
    $('balcaoGrid').innerHTML = bal.map(tileHtml).join('');
    bindTiles($('balcaoGrid'));
  } else {
    $('balcaoWrap').classList.add('hidden');
    $('balcaoGrid').innerHTML = '';
  }
}

function promptOpenMesa(n){
  state.pendingTable = n;
  const label = String(n).padStart(2,'0');
  $('openModalTitle').textContent = 'Abrir Mesa ' + label;
  $('openClientName').value = '';
  $('openErr').classList.add('hidden');
  $('openModal').classList.remove('hidden');
  setTimeout(() => { try { $('openClientName').focus(); } catch {} }, 50);
}

async function confirmOpenMesa(){
  $('openErr').classList.add('hidden');
  const n = state.pendingTable;
  if (!n) return;
  const label = 'Mesa ' + String(n).padStart(2,'0');
  const client = ($('openClientName').value || '').trim();
  const name = client || label;
  try {
    const data = await api('/api/decks', {
      method:'POST',
      body: JSON.stringify({ name, notes: label })
    });
    $('openModal').classList.add('hidden');
    state.pendingTable = null;
    await openDeck(data.deck.id);
  } catch(e) { showErr('openErr', e.message); }
}

async function createDeck(){
  $('createErr').classList.add('hidden');
  const name = ($('newName').value||'').trim();
  if (!name) { showErr('createErr','Informe o nome.'); return; }
  try {
    const data = await api('/api/decks', { method:'POST', body: JSON.stringify({ name }) });
    $('newName').value = '';
    await openDeck(data.deck.id);
  } catch(e) { showErr('createErr', e.message); }
}

async function openDeck(id){
  state.deckId = id;
  state.selectedProduct = null;
  $('prodSearch').value = '';
  $('suggest').classList.add('hidden');
  await refreshDeck();
  show('viewDeck');
}

async function requestPreConta(){
  $('preErr').classList.add('hidden');
  $('preMsg').classList.add('hidden');
  if (!state.deckId) return;
  if (!confirm('Solicitar pré-conta / fechamento para o caixa?')) return;
  try {
    await api('/api/decks/' + state.deckId + '/preconta', { method:'POST', body:'{}' });
    showOk('preMsg', 'Pré-conta solicitada — mesa amarela no caixa.');
    await refreshDeck();
  } catch(e) { showErr('preErr', e.message); }
}

async function refreshDeck(){
  const data = await api('/api/decks/' + state.deckId);
  const d = data.deck;
  $('deckName').textContent = d.name;
  $('deckTotal').textContent = 'Total ' + (d.totalDisplay || '');
  const already = !!d.preconta;
  const btn = $('btnPreConta');
  btn.disabled = already;
  btn.textContent = already ? 'Pré-conta já solicitada' : 'Solicitar Pré-Conta (Fechar)';
  btn.style.opacity = already ? '0.65' : '1';
  const items = d.items || [];
  $('itemList').innerHTML = items.length ? items.map(i => `
    <div class="item" data-id="${i.id}" data-qty="${i.qty}">
      <div style="min-width:0;flex:1">
        <div style="font-weight:600">${escapeHtml(i.name)}</div>
        <div class="muted">${escapeHtml(i.qtyDisplay)} × ${escapeHtml(i.priceDisplay)} = ${escapeHtml(i.subtotalDisplay)}</div>
      </div>
      <div class="item-actions">
        <button type="button" class="btn-secondary btn-minus" title="Diminuir">−</button>
        <span class="qty-val">${escapeHtml(String(i.qty))}</span>
        <button type="button" class="btn-secondary btn-plus" title="Aumentar">+</button>
        <button type="button" class="btn-danger btn-del" title="Excluir">✕</button>
      </div>
    </div>`).join('') : '<div class="muted">Nenhum item ainda.</div>';

  $('itemList').querySelectorAll('.item').forEach(el => {
    const id = +el.dataset.id;
    const qty = parseFloat(el.dataset.qty) || 1;
    el.querySelector('.btn-minus').onclick = () => changeItemQty(id, qty - 1);
    el.querySelector('.btn-plus').onclick = () => changeItemQty(id, qty + 1);
    el.querySelector('.btn-del').onclick = () => removeItem(id, el.querySelector('div div').textContent);
  });
}

async function changeItemQty(itemId, qty){
  try {
    if (qty <= 0) {
      await removeItem(itemId);
      return;
    }
    await api('/api/decks/' + state.deckId + '/items/' + itemId, {
      method:'POST',
      body: JSON.stringify({ qty })
    });
    await refreshDeck();
  } catch(e) { alert(e.message); }
}

async function removeItem(itemId, name){
  const label = name ? `\"${name}\"` : 'este item';
  if (!confirm('Excluir ' + label + ' da comanda?')) return;
  try {
    await api('/api/decks/' + state.deckId + '/items/' + itemId, {
      method:'POST',
      body: JSON.stringify({ action:'delete' })
    });
    await refreshDeck();
  } catch(e) { alert(e.message); }
}

let suggestTimer = null;
function onSearch(){
  clearTimeout(suggestTimer);
  suggestTimer = setTimeout(runSuggest, 160);
}
async function runSuggest(){
  const q = ($('prodSearch').value||'').trim();
  state.selectedProduct = null;
  if (q.length < 1) { $('suggest').classList.add('hidden'); return; }
  try {
    const data = await api('/api/products?q=' + encodeURIComponent(q));
    const list = data.products || [];
    if (!list.length) { $('suggest').classList.add('hidden'); return; }
    $('suggest').classList.remove('hidden');
    $('suggest').innerHTML = list.map(p => `
      <div data-id="${p.id}" data-name="${escapeAttr(p.name)}"
           data-price="${escapeAttr(p.priceDisplay)}"
           data-allows="${p.allowsAvulso ? 1 : 0}"
           data-pav="${escapeAttr(String(p.precoAvulso ?? 0))}"
           data-pmaco="${escapeAttr(String(p.precoMaco ?? p.price ?? 0))}">
        <div style="font-weight:600">${escapeHtml(p.name)}</div>
        <div class="muted">${escapeHtml(p.code||'')} · ${escapeHtml(p.priceDisplay)}${p.allowsAvulso ? ' · Avulso/Maço' : ''}</div>
      </div>`).join('');
    $('suggest').querySelectorAll('div[data-id]').forEach(el => {
      el.onclick = () => {
        state.selectedProduct = {
          id: +el.dataset.id,
          name: el.dataset.name,
          allowsAvulso: el.dataset.allows === '1',
          precoAvulso: parseFloat(el.dataset.pav) || 0,
          precoMaco: parseFloat(el.dataset.pmaco) || 0
        };
        $('prodSearch').value = el.dataset.name;
        $('suggest').classList.add('hidden');
      };
    });
  } catch { $('suggest').classList.add('hidden'); }
}

function moneyBr(v){
  try { return 'R$ ' + Number(v).toFixed(2).replace('.',','); } catch { return 'R$ ' + v; }
}

function openModeModal(product){
  state.pendingModeProduct = product;
  $('modeProductName').textContent = product.name || 'Produto';
  $('modeAvulsoPrice').textContent = moneyBr(product.precoAvulso);
  $('modeMacoPrice').textContent = moneyBr(product.precoMaco);
  $('modeModal').classList.remove('hidden');
}

function closeModeModal(){
  state.pendingModeProduct = null;
  $('modeModal').classList.add('hidden');
}

async function postAddItem(body){
  const data = await api('/api/decks/' + state.deckId + '/items', {
    method:'POST', body: JSON.stringify(body)
  });
  // Term/scan cigarro com avulso: Host pede modalidade (não inseriu ainda).
  if (data.modeRequired) {
    if (data.qty != null && data.qty !== '')
      $('prodQty').value = String(data.qty);
    openModeModal({
      id: data.productId,
      name: data.name,
      allowsAvulso: !!data.allowsAvulso,
      precoAvulso: data.precoAvulso,
      precoMaco: data.precoMaco
    });
    return data;
  }
  $('prodSearch').value = '';
  $('prodQty').value = '1';
  state.selectedProduct = null;
  $('suggest').classList.add('hidden');
  showOk('addMsg', 'Lançado: ' + (data.item?.name || 'item'));
  await refreshDeck();
  return data;
}

async function addItem(){
  $('addErr').classList.add('hidden');
  const qty = parseFloat(($('prodQty').value||'1').replace(',','.')) || 1;
  try {
    if (state.selectedProduct && state.selectedProduct.id) {
      const p = state.selectedProduct;
      if (p.allowsAvulso) {
        openModeModal(p);
        return;
      }
      // Cigarro sem avulso ou comum: Host resolve (cigarro → MAÇO).
      await postAddItem({ productId: p.id, qty });
      return;
    }
    const term = ($('prodSearch').value||'').trim();
    if (!term) { showErr('addErr','Busque e escolha um produto.'); return; }
    // Term/scan: Host pode responder modeRequired para cigarro com avulso.
    await postAddItem({ term, qty });
  } catch(e) { showErr('addErr', e.message); }
}

async function confirmMode(mode){
  const p = state.pendingModeProduct;
  if (!p || !p.id) { closeModeModal(); return; }
  const qty = parseFloat(($('prodQty').value||'1').replace(',','.')) || 1;
  closeModeModal();
  $('addErr').classList.add('hidden');
  try {
    await postAddItem({ productId: p.id, qty, mode });
  } catch(e) { showErr('addErr', e.message); }
}

function escapeHtml(s){
  return String(s||'').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}
function escapeAttr(s){ return escapeHtml(s).replace(/`/g,''); }

$('btnLogin').onclick = login;
$('pinInput').addEventListener('keydown', e => { if (e.key==='Enter') login(); });
$('btnRefresh').onclick = () => loadDecks().catch(e => alert(e.message));
$('btnCreate').onclick = createDeck;
$('newName').addEventListener('keydown', e => { if (e.key==='Enter') createDeck(); });
$('btnOpenCancel').onclick = () => { $('openModal').classList.add('hidden'); state.pendingTable = null; };
$('btnOpenConfirm').onclick = confirmOpenMesa;
$('openClientName').addEventListener('keydown', e => { if (e.key==='Enter') confirmOpenMesa(); });
$('btnModeAvulso').onclick = () => confirmMode('AVULSO');
$('btnModeMaco').onclick = () => confirmMode('MACO');
$('btnModeCancel').onclick = closeModeModal;
$('btnBack').onclick = async () => { show('viewList'); await loadDecks(); };
$('btnNewFromDeck').onclick = async () => {
  show('viewList');
  await loadDecks();
};
if (localStorage.getItem('deckHomeTipDone') === '1')
  $('homeTip').classList.add('hidden');
$('btnDismissTip').onclick = () => {
  localStorage.setItem('deckHomeTipDone', '1');
  $('homeTip').classList.add('hidden');
};
$('prodSearch').addEventListener('input', onSearch);
$('btnAdd').onclick = addItem;
$('prodSearch').addEventListener('keydown', e => { if (e.key==='Enter') addItem(); });
$('btnPreConta').onclick = requestPreConta;

(async function boot(){
  try {
    const st = await fetch('/api/status').then(r => r.json());
    if (st.store) $('storeTitle').textContent = st.store;
  } catch {}
  if (state.pin) {
    $('pinInput').value = state.pin;
    try { await login(); } catch { show('viewLogin'); }
  } else {
    show('viewLogin');
  }
})();
</script>
</body>
</html>
""";
}
