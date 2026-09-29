namespace Vow.Core.Logic
{
    // Successful dash arms one landed basic attack for one second. A miss never calls Consume.
    public sealed class PactAttackWindow
    {
        private double _expiresAt;
        private bool _armed;

        public void Arm(double clock)
        {
            _expiresAt = clock + 1.0;
            _armed = true;
        }

        public bool Consume(double clock)
        {
            if (!_armed || clock > _expiresAt) return false;
            _armed = false;
            return true;
        }

        public bool IsArmed(double clock) => _armed && clock <= _expiresAt;
        public void Clear() { _armed = false; }
    }
}
