using SPTarkov.Server.Core.Models.Common;

namespace OldColoredKeycards.Keycards;

public sealed record ColoredKeycard(
    string Name,
    string ConfigName,
    MongoId Id,
    Func<KeycardPricesConfig, double> Price);

// The six Lab access keycards.
public static class ColoredKeycards
{
    public static readonly IReadOnlyList<ColoredKeycard> All = new List<ColoredKeycard>
    {
        new("Blue Keycard", "blue", new("5c1d0c5f86f7744bb2683cf0"), p => p.Blue),
        new("Red Keycard", "red", new("5c1d0efb86f7744baf2e7b7b"), p => p.Red),
        new("Green Keycard", "green", new("5c1d0dc586f7744baf2e7b79"), p => p.Green),
        new("Black Keycard", "black", new("5c1d0f4986f7744bb01837fa"), p => p.Black),
        new("Violet Keycard", "violet", new("5c1e495a86f7743109743dfb"), p => p.Violet),
        new("Yellow Keycard", "yellow", new("5c1d0d6d86f7744bb2683e1f"), p => p.Yellow)
    };

    public static readonly IReadOnlySet<MongoId> Ids = All.Select(keycard => keycard.Id).ToHashSet();
}
