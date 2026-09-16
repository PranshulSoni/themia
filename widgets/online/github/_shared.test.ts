// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import { subjectUrlToHtml, repoFromRepositoryUrl, mergeAccountResults } from './_shared.ts';

const REPO = 'https://github.com/o/r';

describe('subjectUrlToHtml', () => {
  it('converts a pull subject URL to the /pull/ web URL', () => {
    expect(subjectUrlToHtml('https://api.github.com/repos/o/r/pulls/12', REPO))
      .toBe('https://github.com/o/r/pull/12');
  });

  it('converts an issue subject URL to the /issues/ web URL', () => {
    expect(subjectUrlToHtml('https://api.github.com/repos/o/r/issues/34', REPO))
      .toBe('https://github.com/o/r/issues/34');
  });

  it('strips the /api/v3 prefix for GitHub Enterprise hosts', () => {
    expect(subjectUrlToHtml('https://ghe.corp/api/v3/repos/o/r/issues/7', REPO))
      .toBe('https://ghe.corp/o/r/issues/7');
  });

  it('falls back to repoHtmlUrl when subjectUrl is absent', () => {
    expect(subjectUrlToHtml(null, REPO)).toBe(REPO);
    expect(subjectUrlToHtml(undefined, REPO)).toBe(REPO);
  });

  // [N-22] Regression: an issue-comment API URL carries the comment id, not the
  // issue number, so it cannot be turned into a web URL. It must fall back to
  // repoHtmlUrl rather than silently produce a github.com/…/issues/comments/<id>
  // page that 404s.
  it('[N-22] falls back to repoHtmlUrl for an issue-comment API URL', () => {
    expect(subjectUrlToHtml('https://api.github.com/repos/o/r/issues/comments/12345', REPO))
      .toBe(REPO);
  });

  // [N-22] Regression: a PR-review-comment API URL likewise has no PR number;
  // the old code left the /repos/ prefix in place, producing a 404.
  it('[N-22] falls back to repoHtmlUrl for a PR-review-comment API URL', () => {
    expect(subjectUrlToHtml('https://api.github.com/repos/o/r/pulls/comments/98765', REPO))
      .toBe(REPO);
  });

  it('returns repoHtmlUrl for an unrecognized subject shape', () => {
    expect(subjectUrlToHtml('https://api.github.com/repos/o/r/commits', REPO)).toBe(REPO);
  });
});

describe('repoFromRepositoryUrl', () => {
  it('extracts owner/repo', () => {
    expect(repoFromRepositoryUrl('https://api.github.com/repos/octocat/Hello-World')).toBe('octocat/Hello-World');
  });
});

describe('mergeAccountResults', () => {
  const noop = (): void => {};

  it('[R-18] concatenates fulfilled results and skips rejected ones', () => {
    const results: PromiseSettledResult<number[]>[] = [
      { status: 'fulfilled', value: [1, 2] },
      { status: 'rejected', reason: new Error('HTTP 500') },
      { status: 'fulfilled', value: [3] },
    ];
    expect(mergeAccountResults(results, noop)).toEqual([1, 2, 3]);
  });

  it('returns [] when there are no accounts to fetch', () => {
    expect(mergeAccountResults<number>([], noop)).toEqual([]);
  });

  // [R-03] Regression: when EVERY account fetch fails, the merge must throw so
  // the widget renders its error state — not resolve to [] (which showed the
  // "No pending reviews." empty message on a total network/auth outage).
  it('[R-03] throws the first rejection when every account fails', () => {
    const results: PromiseSettledResult<number[]>[] = [
      { status: 'rejected', reason: new Error('HTTP 401') },
      { status: 'rejected', reason: new Error('HTTP 500') },
    ];
    expect(() => mergeAccountResults(results, noop)).toThrow('HTTP 401');
  });

  it('[R-03] does not throw when at least one account succeeds empty', () => {
    const results: PromiseSettledResult<number[]>[] = [
      { status: 'rejected', reason: new Error('HTTP 401') },
      { status: 'fulfilled', value: [] },
    ];
    expect(mergeAccountResults(results, noop)).toEqual([]);
  });
});
