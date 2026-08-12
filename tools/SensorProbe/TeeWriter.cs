using System.Text;

namespace SensorProbe;

/// <summary>
/// Writes everything to two destinations at once — the console and a file. SensorProbe's whole
/// job is producing a dump someone else can read, and asking a contributor to redirect stdout
/// themselves is friction that loses reports. Console output stays exactly as it was; the file
/// is a side effect they can attach to an issue.
/// </summary>
public sealed class TeeWriter : TextWriter
{
    private readonly TextWriter _console;
    private readonly TextWriter _file;

    public TeeWriter(TextWriter console, TextWriter file)
    {
        _console = console;
        _file = file;
    }

    public override Encoding Encoding => _console.Encoding;

    // Every other TextWriter.Write/WriteLine overload funnels through this one, so overriding
    // it alone is enough to capture all output.
    public override void Write(char value)
    {
        _console.Write(value);
        _file.Write(value);
    }

    public override void Flush()
    {
        _console.Flush();
        _file.Flush();
    }
}
