import { describe, it, expect } from 'vitest';
import { normWinPath, parentDir, samePath, pastableSources } from './dropTarget.ts';

// Regression: dragging files from a folder widget and dropping them back onto
// the SAME folder used to forward a same-directory move to the shell. The guard
// compared raw strings, so a configured root with a trailing slash or forward
// slashes (`D:/Docs/`) did not match the derived source dir (`D:\Docs`), the
// no-op was missed, fs_paste(cut) ran on the file's own folder, and the item
// vanished. [S-54]
describe('folder drop-target: same-folder no-op guard', () => {
  it('normalizes slashes, trailing separators, and case', () => {
    expect(normWinPath('D:/Docs/')).toBe('d:\\docs');
    expect(normWinPath('D:\\Docs')).toBe('d:\\docs');
    expect(normWinPath('C:\\A\\B\\')).toBe('c:\\a\\b');
  });

  it('derives the parent directory', () => {
    expect(parentDir('D:\\Docs\\file.txt')).toBe('d:\\docs');
    expect(parentDir('D:/Docs/file.txt')).toBe('d:\\docs');
  });

  it('treats differently-formatted paths to the same location as equal', () => {
    expect(samePath('D:/Docs/', 'D:\\Docs')).toBe(true);
    expect(samePath('C:\\Users\\Foo', 'c:\\users\\foo\\')).toBe(true);
    expect(samePath('D:\\Docs', 'D:\\Other')).toBe(false);
  });

  it('[S-54] drops a same-folder source even when path formats differ', () => {
    // dest is the configured root with a trailing slash + forward slashes;
    // the dragged file path is the backslash-normalized form.
    expect(pastableSources(['D:\\Docs\\a.txt'], 'D:/Docs/')).toEqual([]);
  });

  it('[S-54] drops a folder dragged onto itself', () => {
    expect(pastableSources(['D:\\Docs\\Sub'], 'D:\\Docs\\Sub')).toEqual([]);
  });

  it('[S-54] keeps real moves into a different directory', () => {
    expect(pastableSources(['D:\\Docs\\a.txt'], 'D:\\Docs\\Sub')).toEqual([
      'D:\\Docs\\a.txt',
    ]);
  });

  it('[S-54] keeps only the genuinely-moved items in a mixed batch', () => {
    expect(
      pastableSources(['D:\\Docs\\a.txt', 'D:\\Other\\b.txt'], 'D:\\Docs'),
    ).toEqual(['D:\\Other\\b.txt']);
  });
});
