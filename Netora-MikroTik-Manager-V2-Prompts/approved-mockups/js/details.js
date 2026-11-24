// UI-only interactions. Backend actions are intentionally not simulated as successful.
const $=id=>document.getElementById(id);
const escapeText=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function notice(message){$('toastArea').innerHTML='<div class="toast-msg">'+escapeText(message)+' — requires backend integration.</div>';}
document.querySelectorAll('.detail-tab').forEach(btn=>btn.addEventListener('click',()=>{
 document.querySelectorAll('.detail-tab').forEach(b=>{b.classList.toggle('active',b===btn);b.setAttribute('aria-selected',String(b===btn))});
 document.querySelectorAll('.detail-tab-pane').forEach(p=>p.hidden=p.dataset.pane!==btn.dataset.tab);
}));
document.addEventListener('click',e=>{const el=e.target.closest('[data-demo]');if(el){e.preventDefault();notice(el.dataset.demo)}});
$('refreshDiagnostics').addEventListener('click',()=>notice('Live MikroTik refresh'));
function setTheme(t){document.documentElement.dataset.bsTheme=t;document.querySelectorAll('[data-theme]').forEach(b=>b.classList.toggle('selected',b.dataset.theme===t));localStorage.setItem('netora-demo-theme',t)}
document.querySelectorAll('[data-theme]').forEach(b=>b.addEventListener('click',()=>setTheme(b.dataset.theme)));
setTheme(localStorage.getItem('netora-demo-theme')==='dark'?'dark':'light');
$('sidebarToggle').addEventListener('click',()=>$('sidebar').classList.toggle('open'));
