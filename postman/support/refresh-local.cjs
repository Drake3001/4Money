// Local-only runner. No production settings, authentication bypasses or TLS bypasses.
const fs = require('node:fs');
const path = require('node:path');
const https = require('node:https');
const { spawn, spawnSync, execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const directory = path.join(root, '.cache/banking-refresh');
const api = path.join(root, 'backend/ZPI4M.Backend/src/ZPI4M.Api');
const database = 'refresh_tests';
const connection = 'Host=127.0.0.1;Port=55439;Database=refresh_tests;Username=refresh_tests;Password=refresh_tests_local';
const psqlArgs = ['-h','127.0.0.1','-p','55439','-U','refresh_tests','-d',database,'-v','ON_ERROR_STOP=1'];
fs.mkdirSync(path.join(directory,'runtime'), { recursive:true, mode:0o700 });
const dbEnv = { ...process.env, PGPASSWORD:'refresh_tests_local' };
function sql(query) { return execFileSync('psql',[...psqlArgs,'-Atc',query],{env:dbEnv,encoding:'utf8'}).trim(); }
function execute(command, args, env = process.env, cwd = root) {
  const result = spawnSync(command,args,{cwd,env,stdio:'inherit'});
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error('Local command failed: '+command);
}
function cli() {
  for (const candidate of [process.env.POSTMAN_CLI,path.join(process.env.HOME || '', '.local/bin/postman'),'postman'].filter(Boolean)) {
    const result = spawnSync(candidate,['--version'],{encoding:'utf8',timeout:5000});
    if (result.status === 0 && /^1\.(?:7[6-9]|[89]\d|\d{3,})\./.test(result.stdout.trim())) return candidate;
  }
  throw new Error('Set POSTMAN_CLI to Postman CLI 1.76+ (not Postman Desktop).');
}
function backend() {
  const env = { ...process.env, DB_LOCAL_CONNECTION_STRING:connection,
    UserAuthentication__SigningKey:'synthetic-refresh-local-signing-key-0123456789',UserAuthentication__AccessTokenLifetimeMinutes:'60',
    ASPNETCORE_ENVIRONMENT:'Development', ASPNETCORE_URLS:'https://localhost:7444',
    Kestrel__Certificates__Default__Path:path.join(directory,'localhost.crt'),Kestrel__Certificates__Default__KeyPath:path.join(directory,'localhost.key'),
    SSL_CERT_FILE:path.join(directory,'localhost.crt'),Tink__ApiBaseUrl:'https://localhost:7443/tink/',Tink__ClientId:'refresh-fixture',
    Tink__ClientSecret:'synthetic-local-secret',Tink__RedirectUri:'https://localhost:7444/api/banking/tink/callback',Tink__ContinuousAccessVerified:'true',
    Banking__Refresh__LeaseSeconds:'30',Banking__Refresh__HeartbeatSeconds:'5',Banking__Refresh__FirstRetrySeconds:'2',
    Banking__Refresh__SubsequentRetrySeconds:'2',Banking__Refresh__PollSeconds:'1',Logging__LogLevel__Default:'Warning' };
  // Program.cs calls Env.Load(): a clean working directory prevents loading API/.env.
  const child = spawn('dotnet',[path.join(api,'bin/Debug/net10.0/ZPI4M.Api.dll'),'--contentRoot',api],
    {cwd:path.join(directory,'runtime'),env,stdio:'inherit'});
  fs.writeFileSync(path.join(directory,'backend.pid'),String(child.pid),{mode:0o600});
  for (const signal of ['SIGINT','SIGTERM']) process.on(signal,()=>child.kill(signal));
  child.on('exit',code=>process.exit(code ?? 1));
}
function apiRequest(route, payload, token) {
  return new Promise((resolve,reject)=> {
    const data = payload === undefined ? null : JSON.stringify(payload);
    const req = https.request('https://localhost:7444'+route,{method:data ? 'POST':'GET',ca:fs.readFileSync(path.join(directory,'localhost.crt')),
      headers:{ ...(data ? {'Content-Type':'application/json','Content-Length':Buffer.byteLength(data)} : {}), ...(token ? {Authorization:'Bearer '+token}: {}) }}, res=> {
      const chunks=[];res.on('data',chunk=>chunks.push(chunk));res.on('end',()=> {
        if (res.statusCode !== 200) return reject(new Error('Fixture API returned HTTP '+res.statusCode));
        try {resolve(JSON.parse(Buffer.concat(chunks).toString()));} catch {reject(new Error('Fixture API returned invalid JSON'));}
      });
    });
    req.setTimeout(10000,()=>req.destroy(new Error('Fixture API timed out')));req.on('error',reject);if(data)req.write(data);req.end();
  });
}
async function login(userLogin) { return (await apiRequest('/api/users/login',{login:userLogin,password:'TestPassword1!'})).accessToken; }
function snapshot() {
  return JSON.parse(sql(`SELECT COALESCE(jsonb_agg(jsonb_build_object('id',t._id,'providerId',t.tink_transaction_id,'amount',t.amount,'status',t.status) ORDER BY t.tink_transaction_id),'[]'::jsonb)
    FROM banking.bank_transaction t JOIN banking.bank_account a ON a._id=t.fk_bank_account_id
    WHERE a.tink_user_id='refresh-fixture-owner' AND a.fk_user_id='10000000-0000-0000-0000-000000000010'`));
}
function accountSnapshot() {
  return JSON.parse(sql(`SELECT COALESCE(jsonb_agg(jsonb_build_object('id',_id,'providerId',tink_account_id,'closed',is_closed,'completed',last_successful_sync_at,'through',transactions_synced_through) ORDER BY tink_account_id),'[]'::jsonb)
    FROM banking.bank_account WHERE tink_user_id='refresh-fixture-owner' AND fk_user_id='10000000-0000-0000-0000-000000000010'`));
}
async function run(prefix) {
  const collection = JSON.parse(fs.readFileSync(path.join(root,'postman/4Money.Banking.postman_collection.json')));
  const folder = collection.item.find(x=>x.name.startsWith(prefix+' —'));
  if (!folder) throw new Error('Unknown refresh folder prefix: '+prefix);
  const template = JSON.parse(fs.readFileSync(path.join(root,'postman/environments/4Money.Local.example.postman_environment.json')));
  const savedFile = path.join(directory,'runner-state.json');
  const saved = fs.existsSync(savedFile) ? JSON.parse(fs.readFileSync(savedFile)) : {};
  const values = { ...saved,baseUrl:'https://localhost:7444',userLogin:'postman.banking',userPassword:'TestPassword1!',
    bankRefreshMockBaseUrl:'http://127.0.0.1:4900/tink',bankRefreshTimeoutSeconds:'2400',bankRefreshMaxPolls:'1200',bankRefreshMinAccounts:'1',
    fourMoneyAccessToken:await login('postman.banking'),bankRefreshOtherUserAccessToken:await login('seed_anna'),bankRefreshNoMappingAccessToken:await login('seed_celina') };
  for(const [key,value] of Object.entries(values)) {
    const entry=template.values.find(x=>x.key===key);if(entry)entry.value=value;else template.values.push({key,value,enabled:true,type:'secret'});
  }
  const environmentFile=path.join(directory,'runner.local.postman_environment.json');
  fs.writeFileSync(environmentFile,JSON.stringify(template),{mode:0o600});
  const before = prefix === '50' ? snapshot() : null;
  const accountsBefore = ['70','72','73','74'].includes(prefix) ? accountSnapshot() : null;
  const log = fs.openSync(path.join(directory,'runner-'+prefix+'.log'),'w',0o600);
  const result=spawnSync(cli(),['collection','run','postman/4Money.Banking.postman_collection.json','-i',folder.name,'-e',environmentFile,
    '--delay-request',process.env.REFRESH_RUNNER_DELAY_MS || '2000','--timeout','2500000','--timeout-request','15000','--timeout-script','5000',
    '--ssl-extra-ca-certs',path.join(directory,'localhost.crt'),'--no-report-events'],{cwd:root,stdio:['ignore',log,log]});
  fs.closeSync(log);
  if (result.error || result.status !== 0) throw new Error('Runner failed for '+prefix+'; inspect local runner-'+prefix+'.log');
  const latest = sql(`SELECT _id FROM banking.bank_refresh WHERE four_money_user_id='10000000-0000-0000-0000-000000000010' ORDER BY created_at DESC LIMIT 1`);
  if (latest) saved.bankRefreshId=latest;
  // CLI does not export its environment. Persist only public job data for the separate restart run.
  if (prefix === '75') {
    const body=await apiRequest('/api/banking/refreshes/'+latest,undefined,await login('postman.banking'));
    saved.bankRefreshRestartId=body.refreshId;
    saved.bankRefreshRestartSavedAccount=JSON.stringify(body.accounts.find(x=>x.status==='SUCCEEDED'));
    if(!saved.bankRefreshRestartSavedAccount)throw new Error('No committed account before restart');
  }
  fs.writeFileSync(savedFile,JSON.stringify(saved,null,2)+'\n',{mode:0o600});
  if(before && before.length) {
    const after=snapshot();
    if(JSON.stringify(before)!==JSON.stringify(after))throw new Error('SQL snapshot changed during idempotence test');
  }
  if(accountsBefore) {
    const after=accountSnapshot();
    if(prefix==='70') {
      const old=accountsBefore.find(x=>x.providerId==='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb');
      const failed=after.find(x=>x.providerId==='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb');
      if(!failed || failed.completed!==(old?.completed ?? null) || failed.through!==(old?.through ?? null))throw new Error('Failed account cursor moved');
    }
    if(prefix==='72' && JSON.stringify(accountsBefore)!==JSON.stringify(after))throw new Error('Discovery failure changed stored accounts');
    if(prefix==='73' && after.some(x=>!x.closed))throw new Error('Missing accounts were not marked closed');
    if(prefix==='74' && after.some(x=>x.closed))throw new Error('Reappearing accounts were not reopened');
  }
  console.log('PASS '+folder.name);
}
async function main() {
  const [command,...args]=process.argv.slice(2);
  if(command==='cert')execute('bash',['postman/support/refresh-cert.sh']);
  else if(command==='simulation')execute(cli(),['simulation','run','postman/simulations/banking-refresh/banking-refresh.sim.yaml','--port','4900','--no-history','--output','ndjson']);
  else if(command==='tls')execute(process.execPath,['postman/support/tink-https.cjs']);
  else if(command==='backend')backend();
  else if(command==='setup') {
    if(sql("SELECT to_regclass('banking.bank_account') IS NOT NULL")!=='f')throw new Error('Fixture DB is not empty; setup refuses to reset it.');
    execute('psql',[...psqlArgs,'-f','database/init/01-schemas.sql','-f','database/init/02-tables.sql'],dbEnv);
    // --connection takes precedence over the .env loaded by the EF startup project.
    execute('dotnet',['ef','database','update','--project','backend/ZPI4M.Backend/src/ZPI4M.Database','--startup-project','backend/ZPI4M.Backend/src/ZPI4M.Api','--context','AppDbContext','--no-build','--connection',connection]);
    execute('psql',[...psqlArgs,'-f','database/seed_local.sql','-c',`UPDATE "user"."user" SET tink_user_id='refresh-fixture-owner' WHERE login='postman.banking';`],dbEnv);
  }
  else if(command==='run') {if(!args.length)throw new Error('Specify folder prefixes, e.g. run 40 50 60 70 71 72 73 74');for(const prefix of args)await run(prefix);}
  else if(command==='restart-test') {
    await run('75');
    const pid=Number(fs.readFileSync(path.join(directory,'backend.pid'),'utf8'));
    const commandLine=fs.readFileSync('/proc/'+pid+'/cmdline','utf8');
    const environment=fs.readFileSync('/proc/'+pid+'/environ','utf8').split('\0');
    if(!commandLine.includes(path.join(api,'bin/Debug/net10.0/ZPI4M.Api.dll')) || !environment.includes('DB_LOCAL_CONNECTION_STRING='+connection))
      throw new Error('Refusing to stop a process that is not the isolated fixture backend');
    process.kill(pid,'SIGKILL');
    const log=fs.openSync(path.join(directory,'restart-backend.log'),'w',0o600);
    const replacement=spawn(process.execPath,[__filename,'backend'],{cwd:root,stdio:['ignore',log,log]});
    try {
      let ready=false;
      for(let attempt=0;attempt<100;attempt++) {
        try { await login('postman.banking');ready=true;break; } catch { await new Promise(resolve=>setTimeout(resolve,200)); }
      }
      if(!ready)throw new Error('Replacement backend did not start');
      console.log('Isolated backend process restarted.');
      await run('76');
    } finally {
      replacement.kill('SIGTERM');
      if(replacement.exitCode === null && replacement.signalCode === null) await new Promise(resolve=>replacement.once('exit',resolve));
      fs.closeSync(log);
    }
  }
  else if(command==='verify') {
    const rows=snapshot();
    const expected=new Set(['a','b'].flatMap(label=>[1,2].map(n=>label+String(n).padStart(31,'0'))));
    if(rows.length!==4 || rows.some(x=>!expected.has(x.providerId)) || new Set(rows.map(x=>x.id)).size!==4 || rows.some(x=>x.status!=='Booked') || rows.filter(x=>Number(x.amount)===-12.34).length!==2 || rows.filter(x=>Number(x.amount)===770).length!==2)
      throw new Error('Unexpected fixture transaction snapshot');
    console.log('SQL verified: 4 unique BOOKED transactions; signed amounts correct; 45-day-old records excluded.');
  } else throw new Error('Usage: node postman/support/refresh-local.cjs cert|setup|simulation|tls|backend|run <prefix...>|restart-test|verify');
}
main().catch(error=> {console.error(error.message);process.exitCode=1;});
