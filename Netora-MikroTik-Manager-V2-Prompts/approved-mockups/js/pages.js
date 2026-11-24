// Netora static design preview — no backend or router calls.
const current=location.pathname.split('/').pop()||'index.html';
document.querySelectorAll('[data-nav]').forEach(a=>{const active=a.dataset.nav===current;a.dataset.current=String(active);if(active)a.setAttribute('aria-current','page')});
function showNotice(msg){const area=document.getElementById('toastArea');if(!area)return;const el=document.createElement('div');el.className='toast-msg';el.textContent=msg+' — demo only; no data was changed.';area.replaceChildren(el)}
document.querySelectorAll('[data-action]').forEach(el=>el.addEventListener('click',()=>showNotice(el.textContent.trim())));
document.querySelectorAll('[data-demo]').forEach(el=>el.addEventListener('click',e=>{e.preventDefault();showNotice(el.dataset.demo||el.textContent.trim())}));
const form=document.getElementById('customerForm');
if(form)form.addEventListener('submit',e=>{e.preventDefault();const msg=document.getElementById('formMessage');if(!form.reportValidity()){msg.textContent='Please fill in the required fields.';return}const d=new FormData(form);msg.textContent='Preview: '+d.get('firstName')+' '+d.get('lastName')+' ('+d.get('username')+') — Manager approval required. No record was saved.'});
const themeButtons=document.querySelectorAll('[data-theme]');
function setTheme(t){document.documentElement.setAttribute('data-bs-theme',t);themeButtons.forEach(b=>b.classList.toggle('selected',b.dataset.theme===t));try{localStorage.setItem('netora-demo-theme',t)}catch{}}
themeButtons.forEach(b=>b.addEventListener('click',()=>setTheme(b.dataset.theme)));
let theme='light';try{theme=localStorage.getItem('netora-demo-theme')==='dark'?'dark':'light'}catch{}setTheme(theme);
const toggle=document.getElementById('sidebarToggle');if(toggle)toggle.addEventListener('click',()=>document.getElementById('sidebar').classList.toggle('open'));
