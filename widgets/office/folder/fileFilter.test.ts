import { describe, it, expect } from 'vitest';
import { parseFilterKeywords, matchesFilter } from './fileFilter.ts';

// Regression for issue #47: the folder filter treated the whole string
// (commas included) as one literal substring. Typing `doc,pdf` searched for
// the literal "doc,pdf", which no filename contains, so the list emptied the
// moment a comma was typed and anything after it had no effect. A comma must
// instead split the input into OR-matched keywords. [S-21]
describe('folder filter: comma-separated keywords', () => {
  it('issue_47 [S-21] parses comma-separated tokens, lowercasing and trimming', () => {
    expect(parseFilterKeywords('Doc, PDF')).toEqual(['doc', 'pdf']);
  });

  it('issue_47 [S-21] ignores empty/whitespace tokens and a trailing comma', () => {
    expect(parseFilterKeywords('doc,,  ,pdf,')).toEqual(['doc', 'pdf']);
    expect(parseFilterKeywords('   ')).toEqual([]);
    expect(parseFilterKeywords('')).toEqual([]);
    expect(parseFilterKeywords(undefined)).toEqual([]);
  });

  it('issue_47 [S-21] a single keyword still filters by substring', () => {
    const kw = parseFilterKeywords('report');
    expect(matchesFilter('quarterly-report.docx', kw)).toBe(true);
    expect(matchesFilter('invoice.pdf', kw)).toBe(false);
  });

  it('issue_47 [S-21] comma input matches files containing AT LEAST ONE keyword (OR)', () => {
    const kw = parseFilterKeywords('doc,pdf');
    // The exact repro: before the fix this emptied the list because no name
    // contains the literal substring "doc,pdf".
    expect(matchesFilter('readme.doc', kw)).toBe(true);
    expect(matchesFilter('invoice.pdf', kw)).toBe(true);
    expect(matchesFilter('budget.docx', kw)).toBe(true); // contains "doc"
    expect(matchesFilter('photo.png', kw)).toBe(false);
    // The literal comma joining is never matched.
    expect(matchesFilter('doc,pdf-weird-name.txt', kw)).toBe(true); // still matches via "doc"
  });

  it('issue_47 [S-21] matching is case-insensitive on both sides', () => {
    const kw = parseFilterKeywords('PDF, Report');
    expect(matchesFilter('Q3-REPORT.TXT', kw)).toBe(true);
    expect(matchesFilter('summary.Pdf', kw)).toBe(true);
  });

  it('issue_47 [S-21] an empty keyword list matches everything (no active filter)', () => {
    expect(matchesFilter('anything.xyz', [])).toBe(true);
    expect(matchesFilter('', [])).toBe(true);
  });

  it('issue_47 [S-21] trailing comma after one word does not empty the list', () => {
    // `report,`: the trailing-comma token is dropped, "report" still filters.
    const kw = parseFilterKeywords('report,');
    expect(kw).toEqual(['report']);
    expect(matchesFilter('annual-report.pdf', kw)).toBe(true);
    expect(matchesFilter('invoice.pdf', kw)).toBe(false);
  });
});
