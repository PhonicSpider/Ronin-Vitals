namespace RoninVitals.Models;

public enum ConsoleLineKind { Command, Output }

/// <summary>One line in the Tech Mode console transcript - either a command (something a
/// diagnostic check did, or something the technician typed) or its result.</summary>
public sealed record ConsoleLine(ConsoleLineKind Kind, string Text);
