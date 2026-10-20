using System.Globalization;
using System.Xml.Linq;
using DenonMarantzAvrNet.Models;
using DenonMarantzAvrNet.Protocol;

namespace DenonMarantzAvrNet.Profiles;

/// <summary>AJAX speaker-distance API shared by supported Denon receiver profiles.</summary>
internal sealed class AjaxSpeakerDistanceProvider(
    DenonSpeakerAjaxOptions options)
    : ISpeakerDistanceProvider
{

    public async Task<DenonSpeakerDistanceConfiguration> GetDistancesAsync(
        DenonProfileContext context,
        CancellationToken cancellationToken)
    {
        var response = await context.HttpTransport.GetStringAsync(
            context.Host,
            options.Scheme,
            options.Port,
            DenonEndpoints.SpeakerDistances(),
            options.AllowUntrustedServerCertificate,
            cancellationToken).ConfigureAwait(false);

        var document = XDocument.Parse(response);
        var distances = document.Descendants("Distances").FirstOrDefault()
            ?? throw new InvalidDataException(
                "Die Receiver-Antwort enthält kein Distances-Element.");

        var unitValue = ParseRequiredInt(
            distances.Element("Unit"),
            "Unit");

        var unit = unitValue switch
        {
            1 => DenonSpeakerDistanceUnit.Feet,
            2 => DenonSpeakerDistanceUnit.Meters,
            _ => throw new InvalidDataException(
                $"Unbekannte Denon-Distanzeinheit: {unitValue}.")
        };

        var rawStep = ParseRequiredInt(
            distances.Element("Step"),
            "Step");

        var ratio = TryParseInt(
            distances.Element("M2FConvertRatio"));

        var speakers = distances
            .Descendants("Speaker")
            .Select(element => new
            {
                Index = (int?)element.Attribute("index"),
                RawValue = TryParseInt(element)
            })
            .Where(item =>
                item.Index is not null &&
                item.RawValue is not null)
            .Select(item => new DenonSpeakerDistance(
                item.Index!.Value,
                RawToMeters(item.RawValue!.Value, unit)))
            .OrderBy(distance => distance.SpeakerIndex)
            .ToArray();

        return new DenonSpeakerDistanceConfiguration(
            unit,
            RawToMeters(rawStep, unit),
            ratio,
            speakers);
        // <-----------
    }

    public async Task SetDistanceAsync(
        DenonProfileContext context,
        int speakerIndex,
        double meters,
        CancellationToken cancellationToken)
    {
        // The wire payload is 1/100 of the displayed receiver unit, not always meters.
        // Read the currently selected unit immediately before every write.
        var current = await GetDistancesAsync(
            context,
            cancellationToken).ConfigureAwait(false);
        if (double.IsNaN(meters) || double.IsInfinity(meters) || meters <= 0 || meters > 18.0)
            throw new ArgumentOutOfRangeException(nameof(meters),
                "Speaker distance must be > 0 m and <= 18 m.");
        if (!current.Speakers.Any(s => s.SpeakerIndex == speakerIndex))
            throw new InvalidDataException("Speaker index not present in current distance configuration.");

        var rawValue = MetersToRaw(meters, current.ReceiverUnit);

        _ = await context.HttpTransport.GetStringAsync(
            context.Host,
            options.Scheme,
            options.Port,
            DenonEndpoints.SetSpeakerDistance(
                speakerIndex,
                rawValue),
            options.AllowUntrustedServerCertificate,
            cancellationToken).ConfigureAwait(false);
    }

    // Denon's AJAX 'Unit' selection uses 1 = feet, 2 = meters.
    // Raw numbers are hundredths of the SELECTED unit.
    internal static double RawToMeters(int rawValue, DenonSpeakerDistanceUnit unit) =>
        rawValue / 100.0 * (unit == DenonSpeakerDistanceUnit.Feet ? 0.3048 : 1.0);

    internal static int MetersToRaw(double meters, DenonSpeakerDistanceUnit unit) =>
        (int)Math.Round(
            meters / (unit == DenonSpeakerDistanceUnit.Feet ? 0.3048 : 1.0) * 100.0,
            MidpointRounding.AwayFromZero);

    private static int ParseRequiredInt(
        XElement? element,
        string name) =>
        TryParseInt(element)
        ?? throw new InvalidDataException(
            $"Die Receiver-Antwort enthält keinen gültigen {name}-Wert.");

    private static int? TryParseInt(XElement? element) =>
        element is not null &&
        int.TryParse(
            element.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
}

internal sealed class UnsupportedSpeakerDistanceProvider(
    string receiverDescription)
    : ISpeakerDistanceProvider
{
    public Task<DenonSpeakerDistanceConfiguration> GetDistancesAsync(
        DenonProfileContext context,
        CancellationToken cancellationToken) =>
        Task.FromException<DenonSpeakerDistanceConfiguration>(
            CreateException());

    public Task SetDistanceAsync(
        DenonProfileContext context,
        int speakerIndex,
        double meters,
        CancellationToken cancellationToken) =>
        Task.FromException(
            CreateException());

    private NotSupportedException CreateException() => new(
        $"Speaker-distance control has not yet been implemented for {receiverDescription}. " +
        "Capture the speaker-distance page requests and add a dedicated provider for this receiver profile.");
}