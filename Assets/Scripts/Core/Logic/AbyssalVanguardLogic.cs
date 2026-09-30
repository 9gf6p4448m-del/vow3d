namespace Vow.Core.Logic
{
    public enum AbyssalVanguardPhase { Dormant, Vanguard, Core, Behemoth, Finished }

    // 只掌管一次性生成、公開核心引導與巨獸存續；生命值由場景中的 CombatTarget 持有。
    public sealed class AbyssalVanguardLogic
    {
        private readonly AbyssalVanguardTuning _tuning;
        private bool _blueInterrupted;
        private bool _redInterrupted;

        public AbyssalVanguardLogic(AbyssalVanguardTuning tuning)
        {
            _tuning = tuning;
            ResetForMatch();
        }

        public AbyssalVanguardPhase Phase { get; private set; }
        public float CoreX => _tuning.CoreX;
        public float CoreZ => _tuning.CoreZ;
        public float CoreRadius => _tuning.CoreRadius;
        public int BehemothOwner { get; private set; }
        public float BlueCoreProgress { get; private set; }
        public float RedCoreProgress { get; private set; }
        public float BehemothRemaining { get; private set; }

        public void ResetForMatch()
        {
            Phase = AbyssalVanguardPhase.Dormant;
            BehemothOwner = CaptureMatchLogic.NeutralFactionId;
            BlueCoreProgress = 0f;
            RedCoreProgress = 0f;
            BehemothRemaining = 0f;
            _blueInterrupted = false;
            _redInterrupted = false;
        }

        public void Tick(float dt, CaptureMatchState state, float matchElapsed,
            float blueX, float blueZ, bool blueAlive, float redX, float redZ, bool redAlive)
        {
            if (state != CaptureMatchState.Active)
            {
                if (state == CaptureMatchState.Ended || Phase != AbyssalVanguardPhase.Dormant)
                    Finish();
                return;
            }

            if (Phase == AbyssalVanguardPhase.Dormant)
            {
                if (matchElapsed >= _tuning.SpawnSeconds) Phase = AbyssalVanguardPhase.Vanguard;
                return;
            }

            if (dt <= 0f) return;

            if (Phase == AbyssalVanguardPhase.Core)
            {
                bool blueInside = blueAlive && InsideCore(blueX, blueZ);
                bool redInside = redAlive && InsideCore(redX, redZ);
                if (!blueInside || _blueInterrupted) BlueCoreProgress = 0f;
                if (!redInside || _redInterrupted) RedCoreProgress = 0f;

                if (blueInside && !redInside && !_blueInterrupted)
                {
                    BlueCoreProgress += dt;
                    if (BlueCoreProgress >= _tuning.CoreChannelSeconds)
                        Claim(CaptureMatchLogic.BlueFactionId);
                }
                else if (redInside && !blueInside && !_redInterrupted)
                {
                    RedCoreProgress += dt;
                    if (RedCoreProgress >= _tuning.CoreChannelSeconds)
                        Claim(CaptureMatchLogic.RedFactionId);
                }

                _blueInterrupted = false;
                _redInterrupted = false;
            }
            else if (Phase == AbyssalVanguardPhase.Behemoth)
            {
                BehemothRemaining -= dt;
                if (BehemothRemaining <= 0f) Finish();
            }
        }

        public void NotifyVanguardDefeated()
        {
            if (Phase != AbyssalVanguardPhase.Vanguard) return;
            Phase = AbyssalVanguardPhase.Core;
            BlueCoreProgress = 0f;
            RedCoreProgress = 0f;
            _blueInterrupted = false;
            _redInterrupted = false;
        }

        public void NotifyHeroDamaged(int side)
        {
            if (Phase != AbyssalVanguardPhase.Core) return;
            if (side == CaptureMatchLogic.BlueFactionId)
            {
                BlueCoreProgress = 0f;
                _blueInterrupted = true;
            }
            else if (side == CaptureMatchLogic.RedFactionId)
            {
                RedCoreProgress = 0f;
                _redInterrupted = true;
            }
        }

        public void NotifyBehemothDefeated()
        {
            if (Phase == AbyssalVanguardPhase.Behemoth) Finish();
        }

        private bool InsideCore(float x, float z)
        {
            return _tuning.IsInCore(x, z);
        }

        private void Claim(int owner)
        {
            Phase = AbyssalVanguardPhase.Behemoth;
            BehemothOwner = owner;
            BehemothRemaining = _tuning.BehemothLifetimeSeconds;
            BlueCoreProgress = 0f;
            RedCoreProgress = 0f;
        }

        private void Finish()
        {
            Phase = AbyssalVanguardPhase.Finished;
            BlueCoreProgress = 0f;
            RedCoreProgress = 0f;
            BehemothRemaining = 0f;
            _blueInterrupted = false;
            _redInterrupted = false;
        }
    }
}
