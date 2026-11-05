namespace EinsteinTelescope.Laser.Editor
{
    /// <summary>
    /// Briefing texts for the demo builder (LaserSetupMenu). They forward to the runtime
    /// <see cref="EinsteinTelescope.Laser.LaserBriefingTexts"/>, the single source of truth that the
    /// LaserBriefingPanel reads at runtime, so the builder never writes old texts back into the scene.
    /// The briefing has no physics page any more: the Physics texts are empty.
    /// "Folded arms" became "Long arms" (see LaserTowerLayout): its names forward to the Long arms texts.
    /// </summary>
    internal static class LaserLevelTexts
    {
        public const string Controls = LaserBriefingTexts.Controls;

        // ----- Level 1 -----------------------------------------------------------------------------

        public const string ObjectiveMichelson = LaserBriefingTexts.ObjectiveMichelson;
        public const string TipMichelson = LaserBriefingTexts.TipMichelson;
        public const string PhysicsMichelson = "";

        // ----- Level 2 -----------------------------------------------------------------------------

        public const string ObjectiveBeamWalking = LaserBriefingTexts.ObjectiveBeamWalking;
        public const string TipBeamWalking = LaserBriefingTexts.TipBeamWalking;
        public const string PhysicsBeamWalking = "";

        // ----- Level 3 -----------------------------------------------------------------------------

        public const string ObjectiveFoldedArms = LaserBriefingTexts.ObjectiveLongArms;
        public const string TipFoldedArms = LaserBriefingTexts.TipLongArms;
        public const string PhysicsFoldedArms = "";
    }
}
