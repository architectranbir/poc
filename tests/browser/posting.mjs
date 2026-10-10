import { chromium } from 'playwright';
import { mkdir } from 'node:fs/promises';
const api='http://localhost:8080';
const projects=await (await fetch(api+'/api/projects')).json();
let snapshot;
for(const p of projects){
  const current=await (await fetch(`${api}/api/projects/${p.id}/wip`)).json();
  if(!current.findings.some(f=>f.severity==='BLOCK')){snapshot=current;break;}
}
if(!snapshot)throw new Error('No billable synthetic fixture');
await mkdir('artifacts/browser',{recursive:true});
const browser=await chromium.launch({headless:true});
const context=await browser.newContext();
await context.tracing.start({screenshots:true,snapshots:true});
const page=await context.newPage();
const failures=[];
page.on('pageerror',error=>failures.push(error.message));
try{
  await page.goto('http://localhost:5173');
  await page.getByRole('heading',{name:'Billing workspace'}).waitFor();
  await page.locator('select').first().selectOption(snapshot.project.id);
  await page.waitForFunction(messages=>messages.every(m=>document.body.innerText.includes(m)),snapshot.findings.map(f=>f.message));
  const selected=snapshot.position.lines.filter(l=>l.bill>0).slice(0,2);
  if(selected.length!==2)throw new Error('Two billable lines required');
  for(const line of selected)await page.getByRole('checkbox',{name:`Select ${line.lineId}`,exact:true}).check();
  await page.getByLabel('Batch rationale',{exact:true}).fill('Reviewed selected WIP in browser');
  const committed=page.waitForResponse(r=>r.url().endsWith('/adjustments/bulk')&&r.request().method()==='POST');
  await page.getByRole('button',{name:'Confirm selected adjustments',exact:true}).click();
  const response=await committed;
  if(!response.ok())throw new Error(await response.text());
  await page.waitForFunction(()=>document.body.innerText.includes('0 entries selected'));
  const adjusted=await (await fetch(`${api}/api/projects/${snapshot.project.id}/wip`)).json();
  for(const line of selected)if(adjusted.position.lines.find(l=>l.lineId===line.lineId).bill!==0)throw new Error('Selected allocation not adjusted');
  const audit=await (await fetch(`${api}/api/projects/${snapshot.project.id}/adjustments`)).json();
  if(!selected.every(l=>audit.some(e=>e.lineId===l.lineId&&e.reason==='Reviewed selected WIP in browser')))throw new Error('Batch audit missing');
  await page.screenshot({path:'artifacts/browser/bulk-adjustments.png',fullPage:true});
  const acknowledgements=page.getByLabel('I have reviewed this finding',{exact:true});
  for(let i=0;i<await acknowledgements.count();i++)await acknowledgements.nth(i).check();
  await page.getByLabel('I have reviewed the VAT treatment',{exact:true}).check();
  if(snapshot.project.currency!==snapshot.project.billingCurrency)
    await page.getByLabel('I confirm the alternate billing currency and synthetic conversion policy',{exact:true}).check();
  await page.getByRole('button',{name:'Create simulated PBD',exact:true}).click();
  await page.getByText(/Status: DRAFT/).waitFor();
  await page.getByRole('button',{name:'Approve exact version',exact:true}).click();
  await page.getByText(/Status: APPROVED/).waitFor();
  await page.getByRole('button',{name:'Confirm posting',exact:true}).click();
  await page.getByText(/Status: POST_PENDING/).waitFor();
  let posted=false;
  for(let attempt=0;attempt<30;attempt++){
    await page.getByRole('button',{name:'Refresh posting status',exact:true}).click();
    const status=page.getByText(/Status: POSTED/);
    try{await status.waitFor({timeout:1000});posted=true;break;}catch{}
  }
  if(!posted)throw new Error('Browser posting did not complete');
  await page.getByText(/Mock SAP posting ID:/).waitFor();
  if(failures.length)throw new Error(failures.join('; '));
  await page.screenshot({path:'artifacts/browser/posted.png',fullPage:true});
  console.log('PASS: React browser project selection, acknowledgements, draft creation, approval and SAP worker posting');
}finally{
  await context.tracing.stop({path:'artifacts/browser/trace.zip'});
  await browser.close();
}
