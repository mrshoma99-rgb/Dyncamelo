(function(){
  var root=document.documentElement;
  try{var saved=localStorage.getItem('dyc-theme');if(saved){root.setAttribute('data-theme',saved);}}catch(e){}
  var toggle=document.getElementById('theme');
  if(toggle){toggle.addEventListener('click',function(){
    var dark=root.getAttribute('data-theme')==='dark'||(!root.getAttribute('data-theme')&&window.matchMedia&&window.matchMedia('(prefers-color-scheme: dark)').matches);
    var next=dark?'light':'dark';root.setAttribute('data-theme',next);
    try{localStorage.setItem('dyc-theme',next);}catch(e){}
  });}
  var box=document.getElementById('search'),out=document.getElementById('results'),base=document.body.getAttribute('data-base')||'';
  if(box&&window.WIKI_INDEX){
    box.addEventListener('input',function(){
      var q=box.value.toLowerCase().split(/\s+/).filter(Boolean);
      if(!q.length){out.style.display='none';return;}
      var hits=[];
      window.WIKI_INDEX.forEach(function(e){
        var hay=(e.t+' '+e.x).toLowerCase(),score=0;
        for(var i=0;i<q.length;i++){if(hay.indexOf(q[i])<0){return;}score+=e.t.toLowerCase().indexOf(q[i])>=0?3:1;}
        hits.push({e:e,s:score});
      });
      hits.sort(function(a,b){return b.s-a.s;});
      out.innerHTML='';
      hits.slice(0,25).forEach(function(h){
        var a=document.createElement('a');a.href=base+h.e.u;
        a.innerHTML='<strong></strong><small></small>';
        a.firstChild.textContent=h.e.t;a.lastChild.textContent=h.e.k+(h.e.d?' - '+h.e.d:'');
        out.appendChild(a);
      });
      if(!hits.length){out.innerHTML='<a href="#"><small>Nothing found.</small></a>';}
      out.style.display='block';
    });
    document.addEventListener('click',function(ev){if(ev.target!==box&&!out.contains(ev.target)){out.style.display='none';}});
  }
  var filter=document.getElementById('node-filter');
  if(filter){
    var rows=document.querySelectorAll('table.all-nodes tbody tr');
    filter.addEventListener('input',function(){
      var q=filter.value.toLowerCase().split(/\s+/).filter(Boolean);
      rows.forEach(function(r){var h=r.getAttribute('data-search')||'';var show=q.every(function(w){return h.indexOf(w)>=0;});r.style.display=show?'':'none';});
    });
  }
})();
