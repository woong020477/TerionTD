using System;
using System.Collections.Generic;
using System.Text;

/// <summary>TCP 조각을 누적해 줄바꿈으로 완성된 JSON 메시지만 꺼내는 프레이밍 버퍼입니다.</summary>
public sealed class JsonLineBuffer
{
    private const int MaxBufferedCharacters = 1024 * 1024;
    private readonly StringBuilder buffer = new();
    public IReadOnlyList<string> Append(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
            return Array.Empty<string>();
        buffer.Append(chunk);
        if (buffer.Length > MaxBufferedCharacters)
        {
            buffer.Clear();
            throw new InvalidOperationException("TCP receive buffer exceeded 1 MB without a complete line.");
        }

        List<string> messages = null;
        while (TryTakeLine(out string message))
        {
            if (string.IsNullOrWhiteSpace(message))
                continue;
            messages ??= new List<string>();
            messages.Add(message);
        }

        return messages ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public void Clear()
    {
        buffer.Clear();
    }

    private bool TryTakeLine(out string line)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\n')
                continue;
            int length = i > 0 && buffer[i - 1] == '\r' ? i - 1 : i;
            line = buffer.ToString(0, length);
            buffer.Remove(0, i + 1);
            return true;
        }

        line = null;
        return false;
    }
}
