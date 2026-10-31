using System.Collections.Generic;

namespace Skybound
{
    /// <summary>
    /// Splits a byte-chunked text stream into lines. Serial reads arrive in
    /// arbitrary chunks, so a message can be split across reads or several can
    /// arrive in one. Overlong lines (e.g. noise at the wrong baud rate) are
    /// discarded up to the next newline instead of growing the buffer forever.
    /// </summary>
    public class LineFramer
    {
        string buffer = "";
        bool discarding;
        public int overflowCount;

        public List<string> Push(string chunk)
        {
            var lines = new List<string>();
            var start = 0;
            for (var i = 0; i < chunk.Length; i++)
            {
                if (chunk[i] != '\n') continue;
                var piece = chunk.Substring(start, i - start);
                start = i + 1;
                if (discarding)
                {
                    discarding = false;
                    buffer = "";
                    continue;
                }
                var line = buffer + piece;
                if (line.EndsWith("\r", System.StringComparison.Ordinal)) line = line.Substring(0, line.Length - 1);
                buffer = "";
                if (line.Length > Protocol.MAX_LINE_LENGTH) overflowCount++;
                else lines.Add(line);
            }
            if (!discarding)
            {
                buffer += chunk.Substring(start);
                if (buffer.Length > Protocol.MAX_LINE_LENGTH)
                {
                    overflowCount++;
                    discarding = true;
                    buffer = "";
                }
            }
            return lines;
        }

        public void Reset()
        {
            buffer = "";
            discarding = false;
        }
    }
}
