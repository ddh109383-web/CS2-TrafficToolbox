// Builds lang/zh-HANT.json from the source strings (the keys of lang/en-US.json) by converting
// Simplified to Traditional characters. Run after adding strings: node tools/make-zh-hant.cjs
const fs=require('fs'),path=require('path');
const pairs='块塊车車规規则則拥擁导導边邊关關闭閉选選择擇图圖与與说說诊診断斷监監测測条條个個标標悬懸键鍵后後点點击擊别別开開两兩设設头頭时時让讓驶駛线線内內执執当當复復处處没沒机機动動请請错錯绕繞辆輛连連续續寻尋败敗临臨记記录錄邻鄰并並志誌变變实實绿綠换換为為从從进進游遊戏戲页頁对對护護业業种種双雙黄黃涂塗节節数數这這带帶货貨几幾单單画畫转轉侧側间間给給过過马馬桥橋远遠筑築称稱还還笔筆价價会會里裡视視现現随隨档檔载載组組长長应應启啟弯彎红紅灯燈锁鎖暂暫无無试試满滿计計着著队隊弃棄刚剛虚虛统統违違钟鐘东東删刪样樣离離缘緣杠槓读讀销銷仅僅资資构構飞飛镜鏡缓緩议議挡擋畅暢观觀细細趋趨势勢紧緊吨噸铁鐵轨軌电電轮輪运運厂廠经經环環竖豎见見费費减減调調范範围圍营營确確认認编編号號缀綴将將响響码碼总總览覽隐隱显顯类類网網据據积積压壓于於输輸颜顏荐薦参參准準国國韩韓纽紐约約华華顿頓伦倫尔爾验驗优優际際挂掛检檢叠疊发發详詳写寫旧舊语語译譯夹夾盖蓋补補';
const map={};for(let i=0;i<pairs.length;i+=2)map[pairs[i]]=pairs[i+1];
const convert=s=>[...s].map(c=>map[c]||c).join('');
const root=path.join(__dirname,'..','lang');
const en=JSON.parse(fs.readFileSync(path.join(root,'en-US.json'),'utf8'));
// Every key is written (also unchanged ones): missing keys would fall back to English.
const out={};for(const k of Object.keys(en))out[k]=convert(k);
fs.writeFileSync(path.join(root,'zh-HANT.json'),JSON.stringify(out,null,1)+'\n');
console.log('zh-HANT entries',Object.keys(out).length,'of',Object.keys(en).length);
