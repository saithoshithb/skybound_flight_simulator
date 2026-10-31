import { MAX_LINE_LENGTH } from './protocol';

/**
 * Splits a byte-chunked text stream into lines. Serial reads arrive in
 * arbitrary chunks, so a message can be split across reads or several can
 * arrive in one. Overlong lines (e.g. noise at the wrong baud rate) are
 * discarded up to the next newline instead of growing the buffer forever.
 */
export class LineFramer {
  private buffer = '';
  private discarding = false;
  overflowCount = 0;

  push(chunk: string): string[] {
    const lines: string[] = [];
    let start = 0;
    for (let i = 0; i < chunk.length; i++) {
      if (chunk[i] !== '\n') continue;
      const piece = chunk.slice(start, i);
      start = i + 1;
      if (this.discarding) {
        this.discarding = false;
        this.buffer = '';
        continue;
      }
      const line = (this.buffer + piece).replace(/\r$/, '');
      this.buffer = '';
      if (line.length > MAX_LINE_LENGTH) this.overflowCount++;
      else lines.push(line);
    }
    if (!this.discarding) {
      this.buffer += chunk.slice(start);
      if (this.buffer.length > MAX_LINE_LENGTH) {
        this.overflowCount++;
        this.discarding = true;
        this.buffer = '';
      }
    }
    return lines;
  }

  reset() {
    this.buffer = '';
    this.discarding = false;
  }
}
