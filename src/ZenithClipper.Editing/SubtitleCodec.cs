using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace ZenithClipper.Editing;

public sealed record SubtitleCue(double StartSeconds, double EndSeconds, string Text);

/// <summary>SRT text interchange. Cue order is preserved, including overlapping cues.</summary>
public static class SubtitleCodec
{
 private static readonly Regex Timing = new(@"^(?<start>\d{2,}:\d{2}:\d{2}[,.]\d{3})\s+-->\s+(?<end>\d{2,}:\d{2}:\d{2}[,.]\d{3})$", RegexOptions.CultureInvariant);

 public static IReadOnlyList<SubtitleCue> ParseSrt(string text)
 {
  ArgumentNullException.ThrowIfNull(text);
  var lines = Normalize(text).TrimStart('\uFEFF').Split('\n');
  var result = new List<SubtitleCue>();
  var index = 0;
  while (index < lines.Length)
  {
   while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
   if (index == lines.Length) break;
   var header = lines[index++].Trim();
   if (header.Length > 0 && header.All(char.IsAsciiDigit))
   {
    if (header.All(c => c == '0') || index == lines.Length) throw Invalid("Missing cue timing or invalid index.");
    header = lines[index++].Trim();
   }
   var match = Timing.Match(header);
   if (!match.Success) throw Invalid("Expected an SRT timestamp range.");
   var start = ParseTimestamp(match.Groups["start"].Value);
   var end = ParseTimestamp(match.Groups["end"].Value);
   if (end < start) throw Invalid("Cue end precedes its start.");
   var content = new List<string>();
   while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index])) content.Add(lines[index++]);
   if (content.Count == 0) throw Invalid("Cue text is empty.");
   result.Add(new SubtitleCue(start, end, string.Join('\n', content)));
  }
  return result.AsReadOnly();
 }

 public static string WriteSrt(IEnumerable<SubtitleCue> cues)
 {
  ArgumentNullException.ThrowIfNull(cues);
  var output = new StringBuilder();
  var index = 0;
  foreach (var cue in cues)
  {
   if (cue is null || !double.IsFinite(cue.StartSeconds) || !double.IsFinite(cue.EndSeconds) || cue.StartSeconds < 0 || cue.EndSeconds < cue.StartSeconds)
    throw new ArgumentException("Cue times must be finite, nonnegative and ordered.", nameof(cues));
   if (cue.Text is null) throw new ArgumentException("Cue text is required.", nameof(cues));
   var text = Normalize(cue.Text);
   if (text.Split('\n').Any(string.IsNullOrWhiteSpace))
    throw new ArgumentException("SRT cannot preserve empty text lines inside a cue.", nameof(cues));
   output.Append(++index).Append('\n').Append(FormatTimestamp(cue.StartSeconds)).Append(" --> ").Append(FormatTimestamp(cue.EndSeconds)).Append('\n').Append(text).Append("\n\n");
  }
  return output.ToString();
 }

 /// <summary>WebVTT for players and sites that want .vtt sidecars.</summary>
 public static string WriteVtt(IEnumerable<SubtitleCue> cues)
 {
  var srt=WriteSrt(cues);var output=new StringBuilder("WEBVTT\n\n");
  foreach(var block in srt.Split("\n\n",StringSplitOptions.RemoveEmptyEntries))
  {
   var lines=block.Split('\n');if(lines.Length<2)continue;
   output.Append(lines[1].Replace(',','.')).Append('\n').Append(string.Join('\n',lines.Skip(2))).Append("\n\n");
  }
  return output.ToString();
 }

 private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
 private static InvalidDataException Invalid(string message) => new(message);
 private static double ParseTimestamp(string value)
 {
  var fields = value.Replace('.', ',').Split(':', ',');
  if (!long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
      !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes > 59 ||
      !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds > 59 ||
      !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var millis)) throw Invalid("Invalid timestamp.");
  try { return checked(hours * 3600000L + minutes * 60000L + seconds * 1000L + millis) / 1000d; }
  catch (OverflowException e) { throw new InvalidDataException("Timestamp is too large.", e); }
 }
 private static string FormatTimestamp(double seconds)
 {
  long total;
  try { total = checked((long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero)); }
  catch (OverflowException e) { throw new ArgumentException("Timestamp is too large.", nameof(seconds), e); }
  return string.Create(CultureInfo.InvariantCulture, $"{total / 3600000:D2}:{total / 60000 % 60:D2}:{total / 1000 % 60:D2},{total % 1000:D3}");
 }
}
