namespace HS.Hero
{
    /// <summary>Signature skills unlocked on the fixed schedule (GDD §4.2): Ch1 Strike I, Ch2 Stance I, Ch3 Finisher I,
    /// Ch4 Strike II + Stance II, Ch5 Finisher II.</summary>
    [System.Flags]
    public enum Signature { None = 0, StrikeI = 1, StanceI = 2, FinisherI = 4, StrikeII = 8, StanceII = 16, FinisherII = 32 }
}
