export interface Quote {
  text: string;
  author: string;
}

// Bundled, offline. No network; "quote of the day" is deterministic per local
// day so it stays stable across reloads and works with no connection.
export const QUOTES: Quote[] = [
  { text: 'The secret of getting ahead is getting started.', author: 'Mark Twain' },
  { text: 'Simplicity is the ultimate sophistication.', author: 'Leonardo da Vinci' },
  { text: 'Well done is better than well said.', author: 'Benjamin Franklin' },
  { text: 'What we know is a drop, what we don’t know is an ocean.', author: 'Isaac Newton' },
  { text: 'It always seems impossible until it’s done.', author: 'Nelson Mandela' },
  { text: 'The only way to do great work is to love what you do.', author: 'Steve Jobs' },
  { text: 'Whether you think you can or you can’t, you’re right.', author: 'Henry Ford' },
  { text: 'Quality is not an act, it is a habit.', author: 'Aristotle' },
  { text: 'The journey of a thousand miles begins with one step.', author: 'Lao Tzu' },
  { text: 'Make it work, make it right, make it fast.', author: 'Kent Beck' },
  { text: 'Perfection is achieved when there is nothing left to take away.', author: 'Antoine de Saint-Exupéry' },
  { text: 'First, solve the problem. Then, write the code.', author: 'John Johnson' },
  { text: 'Knowing yourself is the beginning of all wisdom.', author: 'Aristotle' },
  { text: 'The best time to plant a tree was 20 years ago. The second best is now.', author: 'Chinese Proverb' },
  { text: 'Act as if what you do makes a difference. It does.', author: 'William James' },
  { text: 'Done is better than perfect.', author: 'Sheryl Sandberg' },
];

/** Days since the Unix epoch in LOCAL time; the index seed for "today". */
export function localDayNumber(d: Date): number {
  const local = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  return Math.floor(local.getTime() / 86_400_000);
}

export function quoteForDay(d: Date): Quote {
  return QUOTES[localDayNumber(d) % QUOTES.length];
}
