namespace Vow.Core
{
    // Capture-only control effect; Core never references the Combat assembly.
    public interface ICaptureStunnable
    {
        void ApplyCaptureStun(float seconds);
    }
}
