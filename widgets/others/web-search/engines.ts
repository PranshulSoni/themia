// Closed set of supported search engines. Adding a provider = extend this map
// and the SearchEngine union; the config dropdown + widget read from here.
export type SearchEngine = 'google' | 'bing' | 'duckduckgo' | 'startpage';

export interface EngineDef {
  name: string;
  /** Build the full results URL for a (non-empty, untrimmed) query. */
  url: (query: string) => string;
}

export const ENGINES: Record<SearchEngine, EngineDef> = {
  google:     { name: 'Google',     url: q => `https://www.google.com/search?q=${encodeURIComponent(q)}` },
  bing:       { name: 'Bing',       url: q => `https://www.bing.com/search?q=${encodeURIComponent(q)}` },
  duckduckgo: { name: 'DuckDuckGo', url: q => `https://duckduckgo.com/?q=${encodeURIComponent(q)}` },
  startpage:  { name: 'Startpage',  url: q => `https://www.startpage.com/sp/search?query=${encodeURIComponent(q)}` },
};

export const ENGINE_KEYS = Object.keys(ENGINES) as SearchEngine[];

export function resolveEngine(e: string | undefined): SearchEngine {
  return e && (e in ENGINES) ? (e as SearchEngine) : 'google';
}
