/* Netora static mockup. No MikroTik/API calls or persistence of customer data. */
const seed=[
['Dela Cruz, Juan','juan_dc','Fiber 50 Mbps','NAP-01','ZTEG8A5F3D21','Active','Oct 10, 2026'],
['Santos, Maria','maria_santos','Fiber 100 Mbps','NAP-01','ZTEG8A5F3E19','Active','Oct 11, 2026'],
['Reyes, Pedro','pedro_reyes','Fiber 50 Mbps','NAP-02','HWTCA3B9127F','Active','Oct 12, 2026'],
['Garcia, Ana','ana_garcia','Fiber 30 Mbps','NAP-02','HWTCA3B9184C','Active','Oct 12, 2026'],
['Torres, Miguel','miguel_torres','Fiber 100 Mbps','NAP-03','ZTEG8A5F3E77','Active','Oct 13, 2026'],
['Lim, Carla','carla_lim','Fiber 50 Mbps','NAP-03','ZTEG8A5F3F02','Active','Oct 14, 2026'],
['Ramos, Jose','jose_ramos','Fiber 30 Mbps','UNKNOWN','—','Active','Oct 14, 2026'],
['Villanueva, Rita','rita_villanueva','Fiber 50 Mbps','NAP-04','HWTCA3B92011','Disabled','Oct 15, 2026'],
['Navarro, Edwin','edwin_navarro','Fiber 30 Mbps','UNKNOWN','—','Active','Oct 15, 2026'],
['Ortiz, Liza','liza_ortiz','Fiber 50 Mbps','NAP-04','ZTEG8A5F3F88','Terminated','Sep 20, 2026'],
['Bautista, Carlo','carlo_b','Fiber 100 Mbps','NAP-01','ZTEG8A5F3E71','Active','Oct 17, 2026'],
['Mendoza, Elena','elena_m','Fiber 50 Mbps','NAP-02','ZTEG8A5F3E72','Active','Oct 18, 2026'],
['Aquino, Roberto','roberto_a','Fiber 30 Mbps','NAP-03','HWTCA3B92012','Active','Oct 19, 2026'],
['Cruz, Nina','nina_c','Fiber 100 Mbps','NAP-04','ZTEG8A5F3E73','Active','Oct 20, 2026'],
['Flores, Danilo','danilo_f','Fiber 50 Mbps','NAP-01','ZTEG8A5F3E74','Active','Oct 21, 2026'],
['Rivera, Joy','joy_r','Fiber 30 Mbps','NAP-02','ZTEG8A5F3E75','Active','Oct 22, 2026'],
['Pascual, Eric','eric_p','Fiber 100 Mbps','NAP-03','ZTEG8A5F3E76','Active','Oct 23, 2026'],
['Domingo, Ruth','ruth_d','Fiber 50 Mbps','NAP-04','ZTEG8A5F3E78','Active','Oct 24, 2026'],
['Castillo, Mark','mark_c','Fiber 30 Mbps','NAP-01','ZTEG8A5F3E79','Active','Oct 25, 2026'],
['Valdez, Mia','mia_v','Fiber 50 Mbps','NAP-02','ZTEG8A5F3E80','Active','Oct 26, 2026']];
const icon=(id)=>`<svg class="icon"><use href="#${id}"/></svg>`;
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let page=1;const $=id=>document.getElementById(id);
function filtered(){const q=$('search').value.toLowerCase().trim(),s=$('status').value,n=$('nap').value,p=$('plan').value;return seed.filter(r=>(!q||r.join(' ').toLowerCase().includes(q))&&(s==='All'||r[5]===s)&&(n==='All'||r[3]===n)&&(p==='All'||r[2]===p));}
function render(){const rows=filtered(),size=Number($('pageSize').value),total=Math.max(1,Math.ceil(rows.length/size));page=Math.min(page,total);const start=(page-1)*size,view=rows.slice(start,start+size);$('customerRows').innerHTML=view.map((r,i)=>`<tr><td>${start+i+1}</td><td class="name"><a class="customer-link" href="customer-details.html">${esc(r[0])}</a></td><td>${esc(r[1])}</td><td>${esc(r[2])}</td><td><span class="pill ${r[3]==='UNKNOWN'?'unknown':'nap'}">${esc(r[3])}</span></td><td>${esc(r[4])}</td><td><span class="pill ${r[5].toLowerCase()}">${r[5]==='Active'?icon('wifi'):r[5]==='Terminated'?icon('x'):'Ⅱ'} ${esc(r[5])}</span></td><td>${esc(r[6])}</td><td><div class="actions"><button class="action-btn primary" data-demo="Edit ${esc(r[0])}" title="Edit customer">${icon('edit')}</button><button class="action-btn" data-demo="Diagnostics for ${esc(r[1])}" title="Diagnostics">${icon('pulse')}</button><button class="action-btn" data-demo="More actions for ${esc(r[0])}" title="More actions">${icon('more')}</button></div></td></tr>`).join('')||'<tr><td colspan="9" style="text-align:center;padding:28px">No matching customers found.</td></tr>';
$('showing').textContent=`Showing ${rows.length?start+1:0} to ${Math.min(start+size,rows.length)} of ${rows.length} customers`;
const buttons=[['«',1],['‹',page-1],...Array.from({length:total},(_,i)=>[String(i+1),i+1]),['›',page+1],['»',total]];$('pager').innerHTML=buttons.map(([label,target],i)=>`<button type="button" data-page="${target}" ${target<1||target>total?'disabled':''} class="${i>=2&&i<2+total&&target===page?'active':''}" aria-label="Page ${target}">${label}</button>`).join('');}
$('filters').addEventListener('submit',e=>{e.preventDefault();page=1;render()});$('reset').addEventListener('click',()=>{$('filters').reset();page=1;render()});$('pageSize').addEventListener('change',()=>{page=1;render()});$('pager').addEventListener('click',e=>{const b=e.target.closest('button[data-page]');if(b){page=Number(b.dataset.page);render()}});
function notice(msg){$('toastArea').innerHTML=`<div class="toast-msg">${esc(msg)} — page not implemented in this static mockup.</div>`;setTimeout(()=>$('toastArea').replaceChildren(),3200)};
document.addEventListener('click',e=>{const b=e.target.closest('[data-demo]');if(b){e.preventDefault();notice(b.dataset.demo)}});
function setTheme(t){document.documentElement.dataset.bsTheme=t;document.querySelectorAll('[data-theme]').forEach(b=>b.classList.toggle('selected',b.dataset.theme===t));localStorage.setItem('netora-demo-theme',t)};
document.querySelectorAll('[data-theme]').forEach(b=>b.addEventListener('click',()=>setTheme(b.dataset.theme)));setTheme(localStorage.getItem('netora-demo-theme')==='dark'?'dark':'light');
$('sidebarToggle').addEventListener('click',()=>{$('sidebar').classList.toggle('open')});render();
