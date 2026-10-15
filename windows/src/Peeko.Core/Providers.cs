namespace Peeko.Core;

/// <summary>Seams for the OS. The real ones live in Peeko.Windows; checks inject fakes.</summary>
public interface IForegroundAppProvider
{
    /// <summary>Executable name of the front window's process without ".exe", or null.</summary>
    string? FrontProcessName();

    /// <summary>The front window's title, or null. Only called when a task filters on it.</summary>
    string? FrontWindowTitle();
}

public interface IIdleTimeProvider
{
    double SecondsSinceInput();
}

public readonly record struct ActivitySample(
    DateTimeOffset Now, string? FrontProcess, double SecondsSinceInput, string? WindowTitle = null);
