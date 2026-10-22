using System.Text;

namespace ComTest.Core;

public sealed record BufferDisplay(string Offsets, string Hex, string Ascii);

public static class DataCodec
{
    private static readonly Encoding StrictAscii = Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    public static byte[] Encode(string text, SendingSettings settings)
    {
        settings.Validate();
        byte[] payload;
        try
        {
            payload = settings.Mode switch
            {
                SendMode.ASCII => StrictAscii.GetBytes(text),
                SendMode.UTF8 => new UTF8Encoding(false, true).GetBytes(text),
                SendMode.Hex => Convert.FromHexString(string.Concat(text.Where(c => !char.IsWhiteSpace(c)))),
                _ => throw new ArgumentException("Unsupported send encoding.")
            };
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("This text cannot be encoded. Select UTF8 for non-ASCII characters.");
        }
        catch (FormatException)
        {
            throw new ArgumentException("Enter pairs of hexadecimal digits, for example: 48 65 6C 6C 6F.");
        }
        var suffix = settings.LineEnding switch
        {
            LineEnding.LF => "\n",
            LineEnding.CR => "\r",
            LineEnding.CRLF => "\r\n",
            _ => ""
        };
        return [.. payload, .. Encoding.ASCII.GetBytes(suffix)];
    }

    public static BufferDisplay Format(BufferSnapshot snapshot)
    {
        var offsets = new StringBuilder();
        var hex = new StringBuilder();
        var ascii = new StringBuilder();
        for (var i = 0; i < snapshot.Data.Length; i += 8)
        {
            if (i != 0) { offsets.Append('\n'); hex.Append('\n'); ascii.Append('\n'); }
            offsets.Append((snapshot.DroppedBytes + i).ToString("X6"));
            var end = Math.Min(i + 8, snapshot.Data.Length);
            for (var j = i; j < end; j++)
            {
                if (j != i) hex.Append(' ');
                hex.Append(snapshot.Data[j].ToString("X2"));
                ascii.Append(snapshot.Data[j] is >= 32 and <= 126 ? (char)snapshot.Data[j] : '.');
            }
        }
        return new(offsets.ToString(), hex.ToString(), ascii.ToString());
    }
}
