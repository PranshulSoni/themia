import { describe, it, expect } from 'vitest';
import {
  itemKind, displayName, parseUrlFileBody, parseUrlFileIcon, fileStem,
} from './items.ts';

// Real-world Steam game shortcut (the case that prompted the fix).
const STEAM_URL_FILE = [
  '[{000214A0-0000-0000-C000-000000000046}]',
  'Prop3=19,0',
  '[InternetShortcut]',
  'IDList=',
  'IconIndex=0',
  'URL=steam://rungameid/2211170',
  'IconFile=C:\\Program Files (x86)\\Steam\\steam\\games\\acb3ac128ccab.ico',
].join('\r\n');

describe('shortcut items: classification', () => {
  it('classifies http(s) as url', () => {
    expect(itemKind('https://example.com')).to.equal('url');
    expect(itemKind('http://example.com')).to.equal('url');
  });

  it('[S-73] classifies custom URI protocols (steam://, …) as url, not file', () => {
    expect(itemKind('steam://rungameid/2211170')).to.equal('url');
    expect(itemKind('com.epicgames.launcher://apps/x')).to.equal('url');
    expect(itemKind('file://server/share')).to.equal('url');
  });

  it('classifies local paths and shell/layout correctly', () => {
    expect(itemKind('C:\\Program Files\\app.exe')).to.equal('file');
    expect(itemKind('C:/Users/me/file.txt')).to.equal('file');
    expect(itemKind('\\\\server\\share\\f')).to.equal('file');
    expect(itemKind('shell:RecycleBinFolder')).to.equal('shell');
    expect(itemKind('layout:')).to.equal('layout');
  });
});

describe('shortcut items: .url parsing', () => {
  it('parses the URL line', () => {
    expect(parseUrlFileBody(STEAM_URL_FILE)).to.equal('steam://rungameid/2211170');
  });

  it('parses the IconFile line', () => {
    expect(parseUrlFileIcon(STEAM_URL_FILE)).to.equal(
      'C:\\Program Files (x86)\\Steam\\steam\\games\\acb3ac128ccab.ico');
  });

  it('returns null when there is no IconFile', () => {
    expect(parseUrlFileIcon('[InternetShortcut]\r\nURL=https://x.com')).to.equal(null);
  });

  it('fileStem strips directory and extension', () => {
    expect(fileStem('C:\\Start Menu\\Unrailed 2 Back on Track.url'))
      .to.equal('Unrailed 2 Back on Track');
    expect(fileStem('/a/b/Steam.url')).to.equal('Steam');
  });
});

describe('shortcut items: display name', () => {
  it('a labelled custom-protocol item shows its label, not the opaque URL', () => {
    expect(displayName({ id: '1', path: 'steam://rungameid/2211170', label: 'Unrailed 2' }))
      .to.equal('Unrailed 2');
  });
});
