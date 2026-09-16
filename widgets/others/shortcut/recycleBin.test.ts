import { describe, it, expect, vi, beforeEach } from 'vitest';

const invokeMock = vi.fn();
vi.mock('../../../src/ipc.ts', () => ({ invoke: (...a: unknown[]) => invokeMock(...a) }));

const {
  isRecycleBinPath, fmtBinSize, recycleBinStatus, recycleBinHasItems,
  refreshRecycleBinStatus, __resetRecycleBinStatus, RECYCLE_BIN_PATH,
} = await import('./recycleBin.ts');

describe('[S-75] recycle bin path matching', () => {
  it('matches only the quick-set Recycle Bin URI', () => {
    expect(isRecycleBinPath(RECYCLE_BIN_PATH)).to.equal(true);
    expect(isRecycleBinPath('shell:MyComputerFolder')).to.equal(false);
    expect(isRecycleBinPath('C:\\Users\\me\\file.txt')).to.equal(false);
    expect(isRecycleBinPath(undefined)).to.equal(false);
  });
});

describe('[S-75] bin size formatting', () => {
  it('scales bytes through KB / MB / GB', () => {
    expect(fmtBinSize(0)).to.equal('0 B');
    expect(fmtBinSize(512)).to.equal('512 B');
    expect(fmtBinSize(2048)).to.equal('2.0 KB');
    expect(fmtBinSize(5 * 1024 ** 2)).to.equal('5.0 MB');
    expect(fmtBinSize(3 * 1024 ** 3)).to.equal('3.00 GB');
  });

  it('never renders a negative or non-finite size', () => {
    expect(fmtBinSize(-1)).to.equal('0 B');
    expect(fmtBinSize(NaN)).to.equal('0 B');
  });
});

describe('[S-75] shared status store', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    __resetRecycleBinStatus();
  });

  it('reports "has items" only once a non-empty state is known', async () => {
    expect(recycleBinStatus()).to.equal(null);
    expect(recycleBinHasItems()).to.equal(false);

    invokeMock.mockResolvedValue({ items: 3, bytes: 1024 });
    await refreshRecycleBinStatus();
    expect(recycleBinHasItems()).to.equal(true);
    expect(recycleBinStatus()).to.deep.equal({ items: 3, bytes: 1024 });

    invokeMock.mockResolvedValue({ items: 0, bytes: 0 });
    await refreshRecycleBinStatus(true);
    expect(recycleBinHasItems()).to.equal(false);
  });

  it('collapses concurrent refreshes into one IPC (several widgets, one call)', async () => {
    invokeMock.mockResolvedValue({ items: 1, bytes: 10 });
    await Promise.all([
      refreshRecycleBinStatus(),
      refreshRecycleBinStatus(),
      refreshRecycleBinStatus(),
    ]);
    expect(invokeMock).toHaveBeenCalledTimes(1);
    expect(invokeMock).toHaveBeenCalledWith('recycle_bin_status');
  });

  it('throttles a repeat refresh that lands right after a successful one', async () => {
    invokeMock.mockResolvedValue({ items: 1, bytes: 10 });
    await refreshRecycleBinStatus();
    await refreshRecycleBinStatus();
    expect(invokeMock).toHaveBeenCalledTimes(1);

    // …but an explicit force always re-queries.
    await refreshRecycleBinStatus(true);
    expect(invokeMock).toHaveBeenCalledTimes(2);
  });

  it('keeps the last known state when the query fails (never fabricates empty)', async () => {
    invokeMock.mockResolvedValue({ items: 2, bytes: 20 });
    await refreshRecycleBinStatus();

    invokeMock.mockRejectedValue(new Error('shell unavailable'));
    await refreshRecycleBinStatus(true);
    expect(recycleBinStatus()).to.deep.equal({ items: 2, bytes: 20 });
    expect(recycleBinHasItems()).to.equal(true);
  });
});
