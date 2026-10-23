namespace DenonMarantzAvrNet;

/// <summary>Three-state support result for an <see cref="AvrFeature"/>.</summary>
public enum AvrFeatureSupport
{
    /// <summary>Not yet determined, for example before <see cref="DenonAvrClient.InitializeAsync"/>.</summary>
    Unknown,

    /// <summary>The connected receiver supports the feature.</summary>
    Supported,

    /// <summary>The connected receiver does not support the feature.</summary>
    NotSupported
}