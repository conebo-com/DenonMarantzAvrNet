namespace DenonMarantzAvrNet;

/// <summary>Access direction the library offers for an <see cref="AvrFeature"/>.</summary>
[Flags]
public enum AvrFeatureAccess
{
    None = 0,
    Read = 1,
    Write = 2
}