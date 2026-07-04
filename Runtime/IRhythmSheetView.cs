using System;

namespace Zori.ClapRouter
{
    public interface IRhythmSheetView
    {
        MusicRouterSession Session { get; }

        RhythmSequence CurrentSheet(long audibleFrame);

        event Action<QuantizedHit> PlayerHitJudged;
    }
}
