using Content.Shared._Rat.RoundEndCredits;
using Content.Shared.GameTicking;

namespace Content.Server._Rat.RoundEndCredits;

public sealed class RoundEndCreditsSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEnd);
    }

    private void OnRoundEnd(RoundEndMessageEvent ev)
    {
        var players = new List<CreditsPlayerInfo>();

        foreach (var info in ev.AllPlayersEndInfo)
        {
            if (info.Observer)
                continue;

            var playerName = info.PlayerOOCName;
            var charName = info.PlayerICName ?? "Unknown";
            players.Add(new CreditsPlayerInfo(playerName, charName, info.PlayerNetEntity));
        }

        RaiseNetworkEvent(new RoundEndCreditsEvent(players));
    }
}
