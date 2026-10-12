import json,runpy,time,subprocess
from pathlib import Path
CDP=runpy.run_path('/tmp/grayjay-cdp.py')['CDP'];c=CDP(19228);c.call('Runtime.enable');c.call('Network.enable');c.call('Page.reload',{'ignoreCache':True});time.sleep(1)
cache=Path.home()/'Library/Application Support/Grayjay/playback-components/widevine'
held=cache.with_name('widevine.cancel-test')
cache.rename(held)
report={}
try:
 c.evaluate("(()=>{const e=document.querySelector('input[placeholder=Search]');e.value='https://soundcloud.com/rick-astley-official/never-gonna-give-you-up';e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true}));return true})()")
 for _ in range(100):
  if c.evaluate('!!document.querySelector(\'[aria-label="Set up protected playback"]\')'):break
  time.sleep(.1)
 else:raise RuntimeError('Download prompt did not appear')
 c.evaluate('''(()=>{const d=document.querySelector('[aria-label="Set up protected playback"]');const e=[...d.querySelectorAll('*')].filter(e=>e.textContent==='Cancel').pop();e.click();return true})()''')
 time.sleep(.5)
 report['dismissed']=c.evaluate('!document.querySelector(\'[aria-label="Set up protected playback"]\')')
 report['widevineNotDownloaded']=not cache.exists()
 processes=subprocess.check_output(['ps','-axo','command'],text=True).splitlines()
 report['guestStopped']=not any(line.startswith('/') and '/Contents/Helpers/blink' in line for line in processes)
 report['licenseRequests']=sum(event.get('method')=='Network.responseReceived' and 'widevinelicense' in event['params']['response']['url'].lower() for event in c.events)
 report['success']=report['dismissed'] and report['widevineNotDownloaded'] and report['guestStopped'] and not report['licenseRequests']
finally:
 held.rename(cache)
Path('/tmp/grayjay-production-cancel.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
if not report['success']:raise SystemExit(1)
