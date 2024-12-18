using System.Text;

public class SuppressingTextWriter : TextWriter
{
    private readonly TextWriter _originalWriter;
    private DateTime? _writeFailTime = null;

    public SuppressingTextWriter(TextWriter originalWriter)
    {
        _originalWriter = originalWriter;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        Try(() => _originalWriter.Write(value));
    }

    private void Try(Action act)
    {
        if (_writeFailTime != null)
        {
            var now = DateTime.UtcNow;
            if (now - _writeFailTime < TimeSpan.FromSeconds(10))
                return;

            _writeFailTime = null;
        }

        try
        {
            act();
        }
        catch
        {
            _writeFailTime = DateTime.UtcNow;
        }
    }
}