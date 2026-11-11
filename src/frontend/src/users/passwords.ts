import { wordlist } from "@scure/bip39/wordlists/english.js";

/**
 * A first password an Admin hands over: four random words from the 2,048-word BIP-39 English list, 44 random bits,
 * joined with dashes. Easy to read out, long enough for the password rule, and changed by the person afterwards.
 * Only the word list comes from the library; the randomness is the browser's cryptographic generator.
 *
 * @returns For example `orbit-maple-quiet-lantern`.
 */
export function newPassword(): string {
  const picks = new Uint32Array(4);
  crypto.getRandomValues(picks);

  // 2^32 is a multiple of 2,048, so the remainder picks every word equally often.
  return Array.from(picks, (value) => wordlist[value % wordlist.length]).join("-");
}
