#!/usr/bin/env python3
import argparse
import json
from pathlib import Path
import runpy
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--mode', choices=['soundcloud', 'dash', 'hls-video'], default='soundcloud')
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--to-end', action='store_true', help='Also seek near the end and check the ended event')
parser.add_argument('--expected-bundle', help='Require this built JavaScript filename in the loaded page')
args = parser.parse_args()
CDP = runpy.run_path('/tmp/grayjay-cdp.py')['CDP']
for attempt in range(50):
    try:
        client = CDP(19228)
        break
    except OSError:
        if attempt == 49: raise
        time.sleep(.2)
client.call('Runtime.enable'); client.call('Network.enable')
client.call('Page.reload', {'ignoreCache': True})
for _ in range(50):
    try:
        if client.evaluate('!!document.querySelector("input[placeholder=Search]")'):
            break
    except RuntimeError:
        pass
    time.sleep(.2)
if args.mode != 'soundcloud':
    manifest = 'https://storage.googleapis.com/shaka-demo-assets/angel-one-widevine/' + 'dash.mpd' if args.mode == 'dash' else 'https://storage.googleapis.com/shaka-demo-assets/angel-one-widevine-hls/hls.m3u8'
    media_type = 'application/dash+xml' if args.mode == 'dash' else 'application/vnd.apple.mpegurl'
    client.evaluate('''(()=>{
        const original=window.fetch;
        window.fetch=async(...parameters)=>{
            const response=await original(...parameters);
            if(String(parameters[0]).includes('/details/SourceProxy?')){
                const descriptor=await response.clone().json();
                descriptor.url=%s;descriptor.type=%s;
                descriptor.drm={keySystem:'com.widevine.alpha',licenseUrl:'https://proxy.uat.widevine.com/proxy'};
                return new Response(JSON.stringify(descriptor),{status:response.status,headers:response.headers});
            }
            return response;
        };return true;
    })()''' % (json.dumps(manifest), json.dumps(media_type)))
client.evaluate('''(()=>{const e=document.querySelector('input[placeholder=Search]');e.focus();e.value='https://soundcloud.com/rick-astley-official/never-gonna-give-you-up';e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true}));return true})()''')
snapshots = []
clicked = False
for _ in range(120):

    client.evaluate('''(()=>{if(document.body.innerText.includes('A new update is available for Grayjay Desktop')){
        const e=[...document.querySelectorAll('*')].filter(e=>e.textContent==='Ignore').pop();e?.click();}return true})()''')
    value = client.evaluate('''(()=>{const v=document.querySelector('video');return {dialog:document.querySelector('[aria-label="Set up protected playback"]')?.innerText,ready:v?.readyState,time:v?.currentTime,duration:v?.duration,decodedAudio:v?.webkitAudioDecodedByteCount,decodedVideo:v?.webkitDecodedFrameCount,videoWidth:v?.videoWidth,error:v?.error?.message,mediaKeys:!!v?.mediaKeys}})()''')
    snapshots.append(value)
    if not clicked and value.get('dialog') and 'Download Widevine' in value['dialog']:
        client.evaluate('''(()=>{const d=document.querySelector('[aria-label="Set up protected playback"]');const e=[...d.querySelectorAll('*')].filter(e=>e.textContent==='Download').pop();e.click();return true})()''')
        clicked = True
    if value.get('decodedAudio', 0) > 0 and value.get('time', 0) > 3 and (args.mode == 'soundcloud' or value.get('decodedVideo', 0) > 0):
        break
    time.sleep(.5)
report = {'mode': args.mode, 'firstUsePrompt': clicked, 'snapshots': snapshots,
    'bundles': client.evaluate('[...document.scripts].map(script=>script.src).filter(Boolean)')}
report['exceptions'] = [event['params']['exceptionDetails'].get('text') for event in client.events if event.get('method') == 'Runtime.exceptionThrown']
report['licenses'] = [{'url': event['params']['response']['url'], 'status': event['params']['response']['status']} for event in client.events if event.get('method') == 'Network.responseReceived' and ('license' in event['params']['response']['url'].lower() or 'proxy.uat.widevine.com' in event['params']['response']['url'])]
try:
    last = snapshots[-1]
    if args.expected_bundle:
        assert any(url.endswith('/' + args.expected_bundle) for url in report['bundles']), report['bundles']
    assert last.get('decodedAudio', 0) > 0 and last.get('time', 0) > 3, last
    assert not last['mediaKeys'], 'Unexpected native CDM playback'
    if args.mode != 'soundcloud':
        assert last.get('decodedVideo', 0) > 0 and last.get('videoWidth', 0) > 0, last
    assert any(license['status'] == 200 for license in report['licenses']), report['licenses']
    report['controls'] = client.evaluate('''(async()=>{
        const v=document.querySelector('video'),wait=ms=>new Promise(yes=>setTimeout(yes,ms));
        const resume=async()=>{for(let i=0;i<20;i++){try{await v.play();return;}catch(e){if(e.name!=='AbortError'||v.error)throw e;await wait(100);}}throw Error('Playback did not resume after seeking');};
        v.pause();const paused=v.currentTime;await wait(500);
        const pause=v.paused&&Math.abs(v.currentTime-paused)<.1;
        v.volume=.3;v.muted=true;const volume=v.volume===.3&&v.muted;v.muted=false;
        v.playbackRate=1.5;v.currentTime=30;await resume();await wait(2000);
        return {pause,volume,seek:v.currentTime>31,playbackRate:v.playbackRate===1.5,time:v.currentTime,error:v.error?.message??null};
    })()''')
    assert all(report['controls'][key] for key in ('pause', 'volume', 'seek', 'playbackRate')), report['controls']
    if args.to_end:
        report['end'] = client.evaluate('''(async()=>{
            const v=document.querySelector('video'),wait=ms=>new Promise(yes=>setTimeout(yes,ms));
            let ended=false;v.addEventListener('ended',()=>ended=true,{once:true});
            v.currentTime=v.duration-6;
            for(let i=0;i<20;i++){try{await v.play();break;}catch(e){if(e.name!=='AbortError'||v.error)throw e;await wait(100);}}
            for(let i=0;i<100&&!ended&&!v.error;i++)await wait(200);
            return {ended,time:v.currentTime,duration:v.duration,error:v.error?.message??null};
        })()''')
        assert report['end']['ended'] and not report['end']['error'], report['end']
    report['success'] = True
except Exception as error:
    report['success'] = False; report['error'] = str(error)
args.output.write_text(json.dumps(report, indent=2))
print(json.dumps({key: value for key, value in report.items() if key != 'snapshots'}))
if not report['success']:
    raise SystemExit(1)
